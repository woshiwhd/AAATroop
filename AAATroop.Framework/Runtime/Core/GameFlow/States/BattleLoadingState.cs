using System.Threading;
using Cysharp.Threading.Tasks;

namespace Script.Core
{
    /// <summary>
    /// 战斗加载中：与 Battle 分离，便于在这里做重资源预载而不阻塞「离开大厅」的动画或 UI。
    /// </summary>
    public sealed class BattleLoadingState : IGameFlowState
    {
        private readonly GameFlowOrchestrator _flow;

        public BattleLoadingState(GameFlowOrchestrator flow)
        {
            _flow = flow;
        }

        public async UniTask EnterAsync(CancellationToken ct)
        {
            // BattleLoading 负责重资源预热，避免切入 Battle 首帧抖动。
            await _flow.Context.PreloadBattleAsync(ct);
        }

        public UniTask ExitAsync(CancellationToken ct) => UniTask.CompletedTask;
    }
}
