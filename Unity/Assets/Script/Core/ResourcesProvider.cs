using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Script.Core
{
    /// <summary>
    /// 简易资源提供器：实现 IResourceProvider，用于在运行时从 Resources 加载文本资源（例如 chunk JSON）
    /// - 仅在未提供自定义 provider 时使用
    /// </summary>
    public class ResourcesProvider : IResourceProvider
    {
        public async UniTask<TextAsset> LoadTextAsync(string path, CancellationToken ct = default)
        {
            // 这里直接使用 Unity 的 Resources.LoadTextAsset（同步）并包装为 UniTask，便于在异步流程中使用
            await UniTask.Yield(ct);
            var ta = Resources.Load<TextAsset>(path);
            return ta;
        }
    }
}
