using System.Threading;
using Cysharp.Threading.Tasks;

namespace Script.Core
{
    public sealed class BattleLoadingState : IGameFlowState
    {
        private readonly SceneFlowContext _context;

        public BattleLoadingState(SceneFlowContext context)
        {
            _context = context;
        }

        public async UniTask EnterAsync(CancellationToken ct)
        {
            if (!_context.PreloadBattleGroup) return;

            // BattleLoading 负责重资源预热，避免切入 Battle 首帧抖动。
            await _context.PreloadGroupAsync(_context.BattlePreloadGroupKey, ct);
        }

        public UniTask ExitAsync(CancellationToken ct) => UniTask.CompletedTask;
    }
}
