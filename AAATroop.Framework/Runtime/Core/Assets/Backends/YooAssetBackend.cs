using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;

namespace Script.Core.Assets
{
    public class YooAssetBackend : IAssetBackend
    {
        public AssetBackendType BackendType => AssetBackendType.YooAsset;
        public string BackendName => "YooAsset";

        private readonly string _packageName;
        private ResourcePackage _package;
        private readonly Dictionary<string, AssetHandle> _assetHandles = new Dictionary<string, AssetHandle>();

        public YooAssetBackend(string packageName = "DefaultPackage")
        {
            _packageName = string.IsNullOrWhiteSpace(packageName) ? "DefaultPackage" : packageName;
        }

        public UniTask InitializeAsync(CancellationToken ct = default)
        {
            if (YooAssets.Initialized)
                _package = YooAssets.TryGetPackage(_packageName);
            return UniTask.CompletedTask;
        }

        public bool IsAvailable() => YooAssets.Initialized;

        public async UniTask<UnityEngine.Object> LoadAssetAsync(string key, Type assetType, CancellationToken ct = default)
        {
            if (!IsAvailable())
            {
                UnityEngine.Debug.LogWarning("YooAssetBackend: YooAssets 未初始化，跳过加载。");
                return null;
            }

            _package ??= YooAssets.TryGetPackage(_packageName);
            if (_package == null)
            {
                UnityEngine.Debug.LogWarning($"YooAssetBackend: 未找到 Package: {_packageName}");
                return null;
            }

            var handle = _package.LoadAssetAsync(key, assetType);
            while (!handle.IsDone)
            {
                ct.ThrowIfCancellationRequested();
                await UniTask.Yield(ct);
            }

            if (handle.Status != EOperationStatus.Succeed)
            {
                UnityEngine.Debug.LogWarning($"YooAssetBackend.LoadAssetAsync 失败 key={key}, error={handle.LastError}");
                handle.Release();
                return null;
            }

            if (_assetHandles.TryGetValue(key, out var oldHandle))
                oldHandle.Release();
            _assetHandles[key] = handle;
            return handle.AssetObject;
        }

        public async UniTask<IReadOnlyList<TextAsset>> LoadTextAssetsByGroupAsync(string groupKey, CancellationToken ct = default)
        {
            await UniTask.Yield(ct);
            // YooAsset 一般通过显式 key 列表加载，group 解析交由 AssetGroupsConfig。
            return Array.Empty<TextAsset>();
        }

        public UniTask PreloadAsync(IEnumerable<string> keys, CancellationToken ct = default)
        {
            // 预下载/预加载建议在正式接入 YooAsset 清单后实现。
            return UniTask.CompletedTask;
        }

        public void Release(string key, UnityEngine.Object loadedAsset)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (_assetHandles.TryGetValue(key, out var handle))
            {
                _assetHandles.Remove(key);
                handle.Release();
            }
        }
    }
}
