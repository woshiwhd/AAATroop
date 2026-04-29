using System.Threading;
using Cysharp.Threading.Tasks;

namespace Script.Core
{
    public sealed class BattleState : IGameFlowState
    {
        private readonly SceneFlowContext _context;

        public BattleState(SceneFlowContext context)
        {
            _context = context;
        }

        public async UniTask EnterAsync(CancellationToken ct)
        {
            await _context.LoadSceneByKeyAsync(_context.BattleSceneKey, ct);
        }

        public UniTask ExitAsync(CancellationToken ct) => UniTask.CompletedTask;
    }
}
