using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Script.Core.Assets
{
    public class ResourcesBackend : IAssetBackend
    {
        public AssetBackendType BackendType => AssetBackendType.Resources;
        public string BackendName => "Resources";

        public UniTask InitializeAsync(CancellationToken ct = default) => UniTask.CompletedTask;
        public bool IsAvailable() => true;

        public async UniTask<UnityEngine.Object> LoadAssetAsync(string key, Type assetType, CancellationToken ct = default)
        {
            await UniTask.Yield(ct);
            if (string.IsNullOrEmpty(key) || assetType == null) return null;
            return Resources.Load(key, assetType);
        }

        public async UniTask<IReadOnlyList<TextAsset>> LoadTextAssetsByGroupAsync(string groupKey, CancellationToken ct = default)
        {
            await UniTask.Yield(ct);
            var arr = Resources.LoadAll<TextAsset>(groupKey);
            return arr ?? Array.Empty<TextAsset>();
        }

        public async UniTask PreloadAsync(IEnumerable<string> keys, CancellationToken ct = default)
        {
            if (keys == null) return;
            foreach (var key in keys)
            {
                await LoadAssetAsync(key, typeof(UnityEngine.Object), ct);
            }
        }

        public void Release(string key, UnityEngine.Object loadedAsset)
        {
            // Resources.Load 资源由 Unity 管理，通常不手动释放；
            // 如需积极回收可在上层策略触发 Resources.UnloadUnusedAssets().
        }
    }
}
