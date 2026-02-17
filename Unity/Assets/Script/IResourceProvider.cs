using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Script.Core
{
    /// <summary>
    /// 资源提供器接口：从资源路径异步加载 TextAsset。
    /// 实现约定：如果实现无法支持中途取消，应尽量在调用前检查 ct.IsCancellationRequested 并返回已取消任务；
    /// 返回 null 表示未找到资源。
    /// </summary>
    public interface IResourceProvider
    {
        // Asynchronously load a TextAsset by resource path (Resources.Load style).
        UniTask<TextAsset> LoadTextAsync(string path, CancellationToken ct = default);
    }
}
