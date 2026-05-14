using System.Threading;
using Cysharp.Threading.Tasks;

namespace Script.Core
{
    /// <summary>
    /// Bootstrap：等待场景内初始 Loading 结束（由 <see cref="SceneFlowManager.NotifyBootstrapLoadingComplete"/> 转发至编排器），
    /// 再交由编排进入大厅。首包 SDK/隐私等也可在 Loading 期间由场景脚本驱动完成信号。
    /// </summary>
    public sealed class BootstrapState : IGameFlowState
    {
        private readonly GameFlowOrchestrator _flow;

        public BootstrapState(GameFlowOrchestrator flow)
        {
            _flow = flow;
        }

        public async UniTask EnterAsync(CancellationToken ct)
        {
            // 阻塞直到场景内 BootstrapLoadingFlow（或自定义逻辑）调用 Notify；超时由编排器配置决定。
            await _flow.WaitBootstrapLoadingGateAsync(ct);
        }

        public UniTask ExitAsync(CancellationToken ct) => UniTask.CompletedTask;
    }
}
