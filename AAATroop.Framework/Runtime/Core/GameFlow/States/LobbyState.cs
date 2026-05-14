using System.Threading;
using Cysharp.Threading.Tasks;

namespace Script.Core
{
    /// <summary>
    /// 大厅状态：Enter 时切到大厅场景；场景名/key 由 SceneFlowConfig 与 AssetService 的定位策略解析。
    /// </summary>
    public sealed class LobbyState : IGameFlowState
    {
        private readonly GameFlowOrchestrator _flow;

        public LobbyState(GameFlowOrchestrator flow)
        {
            _flow = flow;
        }

        public async UniTask EnterAsync(CancellationToken ct)
        {
            // 只负责「加载大厅场景」；何时进入 Lobby 由 GameFlowOrchestrator 的剧本决定。
            await _flow.Context.LoadLobbySceneAsync(ct);
        }

        public UniTask ExitAsync(CancellationToken ct) => UniTask.CompletedTask;
    }
}
