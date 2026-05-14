using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Script.Utilities;

namespace Script.Core
{
    /// <summary>
    /// 纯 C#：宏观流程「剧本」——状态之间的顺序、互斥、取消与 Bootstrap 闸门；不依赖 Unity 生命周期。
    /// 场景加载与运行时就绪委托 <see cref="SceneFlowContext"/>。
    /// </summary>
    public sealed class GameFlowOrchestrator : IDisposable
    {
        private readonly SceneFlowConfig _config;
        private readonly SceneFlowContext _context;
        private readonly GameStateMachine _stateMachine = new GameStateMachine();
        // 与 Mono 解耦：Destroy 时 Cancel，避免 Lobby/Battle 切换后仍跑旧编排。
        private readonly CancellationTokenSource _flowCts = new CancellationTokenSource();

        // 防止 EnterBattle / ReturnToLobby / 冷启动并发重入（UniTask 多入口）。
        private bool _isTransitioning;
        // Bootstrap 场景内脚本可能在「闸门创建」前后任意时刻调用 Notify；用 TCS + 提前到达标记避免丢信号。
        private UniTaskCompletionSource _bootstrapLoadGateTcs;
        private bool _bootstrapLoadSignaledBeforeGate;

        public GameFlowOrchestrator(SceneFlowConfig config)
        {
            _config = config;
            _context = new SceneFlowContext(config);
            RegisterStates();
        }

        public SceneFlowContext Context => _context;
        public GameStateMachine StateMachine => _stateMachine;
        public SceneFlowConfig Config => _config;
        public bool AutoEnterLobby => _config != null && _config.autoEnterLobby;
        public float EnterLobbyDelaySeconds => _context.EnterLobbyDelaySeconds;

        // 状态只认识编排器与 Context：剧本在 Orchestrator，单步 IO 在 Context。
        private void RegisterStates()
        {
            _stateMachine.RegisterState(GameFlowState.Initializing, new InitializingState(this));
            _stateMachine.RegisterState(GameFlowState.Bootstrap, new BootstrapState(this));
            _stateMachine.RegisterState(GameFlowState.Lobby, new LobbyState(this));
            _stateMachine.RegisterState(GameFlowState.BattleLoading, new BattleLoadingState(this));
            _stateMachine.RegisterState(GameFlowState.Battle, new BattleState(this));
        }

        // 必须在切到 Bootstrap 状态之前调用：Bootstrap 场景脚本可能在同一帧很早 Start，避免 Notify 早于 TCS 创建。
        private void BeginBootstrapLoadingGate()
        {
            _bootstrapLoadGateTcs = new UniTaskCompletionSource();
            if (_bootstrapLoadSignaledBeforeGate)
            {
                _bootstrapLoadSignaledBeforeGate = false;
                _bootstrapLoadGateTcs.TrySetResult();
            }
        }

        public void NotifyBootstrapLoadingComplete()
        {
            if (_bootstrapLoadGateTcs != null)
                _bootstrapLoadGateTcs.TrySetResult();
            else
                _bootstrapLoadSignaledBeforeGate = true;
        }

        internal async UniTask WaitBootstrapLoadingGateAsync(CancellationToken ct)
        {
            if (_bootstrapLoadGateTcs == null)
            {
                GameLog.LogWarning("GameFlowOrchestrator: Bootstrap 闸门未创建，跳过等待。");
                return;
            }

            UniTask gateTask = _bootstrapLoadGateTcs.Task;
            float timeoutSeconds = _config != null ? _config.bootstrapLoadingGateTimeoutSeconds : 120f;
            if (timeoutSeconds <= 0f)
            {
                await gateTask.AttachExternalCancellation(ct);
                return;
            }

            // 超时仅取消「等待闸门」这一条链：编排仍会继续进大厅，避免忘挂 Loading 脚本时永久卡住。
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            try
            {
                await gateTask.AttachExternalCancellation(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                GameLog.LogError(
                    "GameFlowOrchestrator: Bootstrap 初始 Loading 等待超时，仍将继续进入大厅。请确认已挂载 BootstrapLoadingFlow，或适时调用 NotifyBootstrapLoadingComplete。");
            }
        }

        // 冷启动剧本：Initializing（资源就绪）→ Bootstrap（场景内 Loading 闸门）→ 可选间隔 → Lobby（Single 切场景）。
        public async UniTask EnterLobbyFromBootstrapAsync()
        {
            await RunTransitionAsync(async ct =>
            {
                if (_stateMachine.CurrentState == GameFlowState.None)
                {
                    await _stateMachine.ChangeStateAsync(GameFlowState.Initializing, ct);
                    BeginBootstrapLoadingGate();
                    await _stateMachine.ChangeStateAsync(GameFlowState.Bootstrap, ct);
                }

                if (EnterLobbyDelaySeconds > 0f)
                    await UniTask.Delay((int)(EnterLobbyDelaySeconds * 1000f), cancellationToken: ct);

                await _stateMachine.ChangeStateAsync(GameFlowState.Lobby, ct);
            });
        }

        // 先 BattleLoading（可选预载）再 Battle，把重 IO 与切场景拆开，减轻大厅最后一帧压力。
        public void EnterBattle()
        {
            RunTransitionAsync(async ct =>
            {
                await _stateMachine.ChangeStateAsync(GameFlowState.BattleLoading, ct);
                await _stateMachine.ChangeStateAsync(GameFlowState.Battle, ct);
            }).Forget();
        }

        public void ReturnToLobby()
        {
            RunTransitionAsync(async ct =>
            {
                await _stateMachine.ChangeStateAsync(GameFlowState.Lobby, ct);
            }).Forget();
        }

        // 所有对外流程入口共用同一互斥与 ct，避免状态机 Exit/Enter 与 LoadScene 交错。
        private async UniTask RunTransitionAsync(Func<CancellationToken, UniTask> transition)
        {
            if (_isTransitioning)
            {
                GameLog.LogWarning("GameFlowOrchestrator: 状态切换进行中，忽略重复请求。");
                return;
            }

            _isTransitioning = true;
            try
            {
                await transition(_flowCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Dispose 时 Cancel：正常中断。
            }
            finally
            {
                _isTransitioning = false;
            }
        }

        // 与 SceneFlowManager.OnDestroy 对齐：取消在飞的编排，避免对象销毁后仍 await。
        public void Dispose()
        {
            _flowCts?.Cancel();
            _flowCts?.Dispose();
        }
    }
}
