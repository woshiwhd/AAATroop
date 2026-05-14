using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Script.Core
{
    /// <summary>
    /// 挂在 Bootstrap 场景：负责「初始 Loading」结束时机，结束时调用 <see cref="SceneFlowManager.NotifyBootstrapLoadingComplete"/>。
    /// 可用计时自动结束，或由你的进度逻辑在适当时机调用 <see cref="CompleteLoadingManually"/>。
    /// </summary>
    public sealed class BootstrapLoadingFlow : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("勾选后经过 autoCompleteDelaySeconds 自动通知 Loading 完成；关闭则需自行调用 CompleteLoadingManually。")]
        private bool useTimerAutoComplete = true;

        [SerializeField]
        [Tooltip("自动完成前的最短展示时间（秒），忽略 timeScale。")]
        private float autoCompleteDelaySeconds = 0.8f;

        private void Start()
        {
            if (useTimerAutoComplete)
                RunTimerAsync().Forget();
        }

        private async UniTaskVoid RunTimerAsync()
        {
            if (autoCompleteDelaySeconds > 0f)
                await UniTask.Delay(TimeSpan.FromSeconds(autoCompleteDelaySeconds), ignoreTimeScale: true);
            SceneFlowManager.Instance?.NotifyBootstrapLoadingComplete();
        }

        /// <summary>由自定义加载逻辑（进度条 100%、资源就绪等）调用。</summary>
        public void CompleteLoadingManually()
        {
            SceneFlowManager.Instance?.NotifyBootstrapLoadingComplete();
        }
    }
}
