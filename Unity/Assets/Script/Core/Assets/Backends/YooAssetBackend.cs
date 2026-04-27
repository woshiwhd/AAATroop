using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Script.Core.Assets
{
    public class YooAssetBackend : IAssetBackend
    {
        public AssetBackendType BackendType => AssetBackendType.YooAsset;
        public string BackendName => "YooAsset";

        private Type _yooAssetsType;
        private readonly string _packageName;

        public YooAssetBackend(string packageName = "DefaultPackage")
        {
            _packageName = string.IsNullOrWhiteSpace(packageName) ? "DefaultPackage" : packageName;
        }

        public UniTask InitializeAsync(CancellationToken ct = default)
        {
            _yooAssetsType = Type.GetType("YooAsset.YooAssets, YooAsset");
            return UniTask.CompletedTask;
        }

        public bool IsAvailable() => _yooAssetsType != null;

        public async UniTask<UnityEngine.Object> LoadAssetAsync(string key, Type assetType, CancellationToken ct = default)
        {
            await UniTask.Yield(ct);
            if (!IsAvailable())
            {
                UnityEngine.Debug.LogWarning("YooAssetBackend: YooAsset package not found, load skipped.");
                return null;
            }

            // 这里不直接绑定 YooAsset 类型，避免在未安装包时编译失败。
            // 项目接入 YooAsset 后，可替换为强类型调用（Package.LoadAssetAsync<T>）。
            try
            {
                MethodInfo getPackage = _yooAssetsType.GetMethod(
                    "GetPackage",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string) },
                    null);
                if (getPackage == null) return null;

                var pkg = getPackage.Invoke(null, new object[] { _packageName });
                if (pkg == null) return null;

                MethodInfo loadMethod = pkg.GetType().GetMethod("LoadAssetSync", new[] { typeof(string), typeof(Type) });
                if (loadMethod == null) return null;
                return loadMethod.Invoke(pkg, new object[] { key, assetType }) as UnityEngine.Object;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"YooAssetBackend.LoadAssetAsync failed: {e.Message}");
                return null;
            }
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
            // 由 YooAsset handle 释放；当前反射兜底版本不跟踪 handle。
        }
    }
}
