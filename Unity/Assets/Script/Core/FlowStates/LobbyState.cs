using System.Threading;
using Cysharp.Threading.Tasks;

namespace Script.Core
{
    public sealed class LobbyState : IGameFlowState
    {
        private readonly SceneFlowContext _context;

        public LobbyState(SceneFlowContext context)
        {
            _context = context;
        }

        public async UniTask EnterAsync(CancellationToken ct)
        {
            await _context.LoadSceneByKeyAsync(_context.LobbySceneKey, ct);
        }

        public UniTask ExitAsync(CancellationToken ct) => UniTask.CompletedTask;
    }
}
