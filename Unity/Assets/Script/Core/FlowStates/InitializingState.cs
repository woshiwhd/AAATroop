using System.Threading;
using Cysharp.Threading.Tasks;
using Script.Utilities;

namespace Script.Core
{
    /// <summary>
    /// 初始化状态：统一完成资源运行时准备，避免后续状态在未就绪时触发场景加载。
    /// </summary>
    public sealed class InitializingState : IGameFlowState
    {
        private readonly SceneFlowContext _context;

        public InitializingState(SceneFlowContext context)
        {
            _context = context;
        }

        public async UniTask EnterAsync(CancellationToken ct)
        {
            bool ok = await _context.EnsureRuntimeReadyAsync(ct);
            if (!ok)
                GameLog.LogError("InitializingState: 运行时初始化失败，后续状态可能无法正确加载。");
        }

        public UniTask ExitAsync(CancellationToken ct) => UniTask.CompletedTask;
    }
}
