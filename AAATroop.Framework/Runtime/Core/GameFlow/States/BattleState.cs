using System.Threading;
using Cysharp.Threading.Tasks;

namespace Script.Core
{
    /// <summary>
    /// 战斗状态：Enter 时切换到战斗场景；具体生成单位等放在战斗场景内脚本，避免本状态过重。
    /// </summary>
    public sealed class BattleState : IGameFlowState
    {
        private readonly GameFlowOrchestrator _flow;

        public BattleState(GameFlowOrchestrator flow)
        {
            _flow = flow;
        }

        public async UniTask EnterAsync(CancellationToken ct)
        {
            // 战斗场景切换经 SceneFlowContext / AssetService（含 Yoo 路径约定）。
            await _flow.Context.LoadBattleSceneAsync(ct);
        }

        public UniTask ExitAsync(CancellationToken ct) => UniTask.CompletedTask;
    }
}
