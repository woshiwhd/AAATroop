using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Script.Core.Assets
{
    public interface IAssetBackend
    {
        AssetBackendType BackendType { get; }
        string BackendName { get; }

        UniTask InitializeAsync(CancellationToken ct = default);
        bool IsAvailable();

        UniTask<UnityEngine.Object> LoadAssetAsync(string key, Type assetType, CancellationToken ct = default);
        UniTask<IReadOnlyList<TextAsset>> LoadTextAssetsByGroupAsync(string groupKey, CancellationToken ct = default);

        UniTask PreloadAsync(IEnumerable<string> keys, CancellationToken ct = default);
        void Release(string key, UnityEngine.Object loadedAsset);
    }
}
