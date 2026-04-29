using System.Threading;
using Cysharp.Threading.Tasks;
using Script.Utilities;
using UnityEngine;

namespace Script.Core
{
    /// <summary>
    /// 全局场景流控制器（建议挂在 Bootstrap 的 GlobalRoot，并 DontDestroyOnLoad）。
    /// 统一管理 Bootstrap/Lobby/Battle 的切换，避免分散 Entry 逻辑。
    /// </summary>
    public class SceneFlowManager : MonoBehaviour
    {
        public static SceneFlowManager Instance { get; private set; }
        [SerializeField] private SceneFlowConfig config;

        private readonly GameStateMachine _stateMachine = new GameStateMachine();
        public GameStateMachine StateMachine => _stateMachine;
        private bool _isTransitioning;
        private CancellationTokenSource _flowCts;
        private SceneFlowContext _context;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            _flowCts = new CancellationTokenSource();
            _context = new SceneFlowContext(config);
            RegisterStates();
        }

        private void Start()
        {
            if (config != null && config.autoEnterLobby)
                EnterLobbyFromBootstrapAsync().Forget();
        }

        public async UniTask EnterLobbyFromBootstrapAsync()
        {
            await RunTransitionAsync(async ct =>
            {
                if (StateMachine.CurrentState == GameFlowState.None)
                {
                    await StateMachine.ChangeStateAsync(GameFlowState.Initializing, ct);
                    await StateMachine.ChangeStateAsync(GameFlowState.Bootstrap, ct);
                }

                // Bootstrap 到 Lobby 的短延时用于稳定首屏初始化时序，避免首帧切场景抖动。
                if (_context.EnterLobbyDelaySeconds > 0f)
                    await UniTask.Delay((int)(_context.EnterLobbyDelaySeconds * 1000f), cancellationToken: ct);

                await StateMachine.ChangeStateAsync(GameFlowState.Lobby, ct);
            });
        }

        public void EnterBattle()
        {
            RunTransitionAsync(async ct =>
            {
                await StateMachine.ChangeStateAsync(GameFlowState.BattleLoading, ct);
                await StateMachine.ChangeStateAsync(GameFlowState.Battle, ct);
            }).Forget();
        }

        public void ReturnToLobby()
        {
            RunTransitionAsync(async ct =>
            {
                await StateMachine.ChangeStateAsync(GameFlowState.Lobby, ct);
            }).Forget();
        }

        private async UniTask RunTransitionAsync(System.Func<CancellationToken, UniTask> action)
        {
            if (_isTransitioning)
            {
                GameLog.LogWarning("SceneFlowManager: 场景流切换进行中，忽略重复请求。");
                return;
            }

            _isTransitioning = true;
            try
            {
                await action(_flowCts.Token);
            }
            catch (System.OperationCanceledException) { }
            finally
            {
                _isTransitioning = false;
            }
        }

        private void RegisterStates()
        {
            _stateMachine.RegisterState(GameFlowState.Initializing, new InitializingState(_context));
            _stateMachine.RegisterState(GameFlowState.Bootstrap, new BootstrapState());
            _stateMachine.RegisterState(GameFlowState.Lobby, new LobbyState(_context));
            _stateMachine.RegisterState(GameFlowState.BattleLoading, new BattleLoadingState(_context));
            _stateMachine.RegisterState(GameFlowState.Battle, new BattleState(_context));
        }

        private void OnDestroy()
        {
            if (_flowCts != null)
            {
                _flowCts.Cancel();
                _flowCts.Dispose();
                _flowCts = null;
            }
        }

        private sealed class InitializingState : IGameFlowState
        {
            private readonly SceneFlowContext _context;

            public InitializingState(SceneFlowContext context)
            {
                _context = context;
            }

            public async UniTask EnterAsync(CancellationToken ct)
            {
                // 初始化状态统一做运行时准备，保证后续场景状态进入时依赖已就绪。
                bool ok = await _context.EnsureRuntimeReadyAsync(ct);
                if (!ok)
                    GameLog.LogError("SceneFlowManager: InitializingState failed.");
            }

            public UniTask ExitAsync(CancellationToken ct) => UniTask.CompletedTask;
        }
    }
}
