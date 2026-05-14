using System.Threading;
using Cysharp.Threading.Tasks;
using Script.Utilities;

namespace Script.Core
{
    /// <summary>
    /// 初始化状态：在任意「要加载 Yoo 场景/资源」的状态之前执行，保证清单与 AssetService 已就绪。
    /// </summary>
    public sealed class InitializingState : IGameFlowState
    {
        private readonly GameFlowOrchestrator _flow;

        public InitializingState(GameFlowOrchestrator flow)
        {
            _flow = flow;
        }

        public async UniTask EnterAsync(CancellationToken ct)
        {
            // 只走 Context：与 Mono 解耦，便于同一套状态在测试里 new Orchestrator 复用。
            // 失败时不抛异常中断状态机：由日志提示，后续 Load 仍会失败，便于在编辑器里一眼看到初始化问题。
            bool ok = await _flow.Context.EnsureRuntimeReadyAsync(ct);
            if (!ok)
                GameLog.LogError("InitializingState: 运行时初始化失败，后续状态可能无法正确加载。");
        }

        public UniTask ExitAsync(CancellationToken ct) => UniTask.CompletedTask;
    }
}
