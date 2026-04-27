using System.Threading;
using Cysharp.Threading.Tasks;
using Script.Core.Assets;
using UnityEngine;

namespace Script.Core
{
    /// <summary>
    /// 兼容旧逻辑：转调 IAssetService（若未初始化则回退 ResourcesBackend）。
    /// </summary>
    public class ResourcesProvider : IResourceProvider
    {
        public async UniTask<TextAsset> LoadTextAsync(string path, CancellationToken ct = default)
        {
            var service = AssetServiceLocator.Current;
            if (service != null) return await service.LoadAssetAsync<TextAsset>(path, ct);

            // 兜底：在未初始化 AssetService 时，直接通过 ResourcesBackend 读取
            var backend = new ResourcesBackend();
            await backend.InitializeAsync(ct);
            return await backend.LoadAssetAsync(path, typeof(TextAsset), ct) as TextAsset;
        }
    }
}
