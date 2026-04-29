using System.Threading;
using Cysharp.Threading.Tasks;

namespace Script.Core
{
    public sealed class BootstrapState : IGameFlowState
    {
        public UniTask EnterAsync(CancellationToken ct) => UniTask.CompletedTask;
        public UniTask ExitAsync(CancellationToken ct) => UniTask.CompletedTask;
    }
}
