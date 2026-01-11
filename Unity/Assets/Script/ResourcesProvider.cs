using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 简单的资源提供器：基于 UnityEngine.Resources
/// 说明：Resources.Load 是 Unity API，必须在主线程调用。本实现保证不论调用者在哪个线程，都会先切回主线程再执行 Resources.Load。
/// 注意：因为 Resources.Load 是同步操作，无法在执行中被 CancellationToken 中断；
/// 如果传入的 CancellationToken 已经处于取消状态，会立即返回一个已取消的任务。
/// </summary>
public class ResourcesProvider : IResourceProvider
{
    public async UniTask<TextAsset> LoadTextAsync(string path, CancellationToken ct = default)
    {
        // 如果调用者已经请求取消，马上返回已取消的任务
        if (ct.IsCancellationRequested)
        {
            return await UniTask.FromCanceled<TextAsset>(ct);
        }

        // 确保在主线程执行 Unity API
        await UniTask.SwitchToMainThread(ct);

        // Resources.Load 是同步的；在主线程直接执行并返回结果
        TextAsset ta = Resources.Load<TextAsset>(path);
        return ta;
    }
}
