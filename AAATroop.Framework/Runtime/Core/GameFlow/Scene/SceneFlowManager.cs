using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Script.Core
{
    /// <summary>
    /// Unity 生命周期薄壳：跨场景常驻、挂 <see cref="SceneFlowConfig"/>、对外稳定入口。
    /// 宏观剧本与状态机在 <see cref="GameFlowOrchestrator"/> 中。
    /// </summary>
    [DisallowMultipleComponent]
    public class SceneFlowManager : MonoBehaviour
    {
        public static SceneFlowManager Instance { get; private set; }

        [SerializeField] private SceneFlowConfig config;

        private GameFlowOrchestrator _orchestrator;

        public GameStateMachine StateMachine => _orchestrator?.StateMachine;

        /// <summary>编排器：测试或扩展时可读；一般通过本类转发 API 即可。</summary>
        public GameFlowOrchestrator Orchestrator => _orchestrator;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            // Lobby/Battle Single 会卸载场景物体；编排与状态机必须跟本组件跨场景存活。
            DontDestroyOnLoad(gameObject);
            _orchestrator = new GameFlowOrchestrator(config);
        }

        private void Start()
        {
            // 首包可选自动冷启动；关闭时由外部显式调用 EnterLobbyFromBootstrapAsync。
            if (_orchestrator != null && _orchestrator.AutoEnterLobby)
                _orchestrator.EnterLobbyFromBootstrapAsync().Forget();
        }

        // 以下对外 API 保持历史签名：UI/场景脚本只依赖 Instance，不直接引用 GameFlowOrchestrator。

        public void NotifyBootstrapLoadingComplete() => _orchestrator?.NotifyBootstrapLoadingComplete();

        public UniTask EnterLobbyFromBootstrapAsync() =>
            _orchestrator != null ? _orchestrator.EnterLobbyFromBootstrapAsync() : UniTask.CompletedTask;

        public void EnterBattle() => _orchestrator?.EnterBattle();

        public void ReturnToLobby() => _orchestrator?.ReturnToLobby();

        public SceneFlowConfig Config => _orchestrator != null ? _orchestrator.Config : config;
        public bool AutoEnterLobby => _orchestrator != null && _orchestrator.AutoEnterLobby;
        public float EnterLobbyDelaySeconds => _orchestrator != null ? _orchestrator.EnterLobbyDelaySeconds : 0f;

        public UniTask<bool> EnsureRuntimeReadyAsync(CancellationToken ct = default) =>
            _orchestrator != null ? _orchestrator.Context.EnsureRuntimeReadyAsync(ct) : UniTask.FromResult(false);

        public UniTask<bool> LoadBootstrapSceneAsync(CancellationToken ct = default) =>
            _orchestrator != null ? _orchestrator.Context.LoadBootstrapSceneAsync(ct) : UniTask.FromResult(false);

        public UniTask<bool> LoadLobbySceneAsync(CancellationToken ct = default) =>
            _orchestrator != null ? _orchestrator.Context.LoadLobbySceneAsync(ct) : UniTask.FromResult(false);

        public UniTask<bool> LoadBattleSceneAsync(CancellationToken ct = default) =>
            _orchestrator != null ? _orchestrator.Context.LoadBattleSceneAsync(ct) : UniTask.FromResult(false);

        public UniTask PreloadBattleAsync(CancellationToken ct = default) =>
            _orchestrator != null ? _orchestrator.Context.PreloadBattleAsync(ct) : UniTask.CompletedTask;

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            // 取消编排中的 UniTask，避免切场景后仍持有旧流程。
            _orchestrator?.Dispose();
            _orchestrator = null;
        }
    }
}
