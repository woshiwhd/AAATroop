using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Script.Core.Assets
{
    public class AddressablesBackend : IAssetBackend
    {
        public AssetBackendType BackendType => AssetBackendType.Addressables;
        public string BackendName => "Addressables";

        private Type _addressablesType;

        public UniTask InitializeAsync(CancellationToken ct = default)
        {
            _addressablesType = Type.GetType("UnityEngine.AddressableAssets.Addressables, Unity.Addressables");
            return UniTask.CompletedTask;
        }

        public bool IsAvailable() => _addressablesType != null;

        public async UniTask<UnityEngine.Object> LoadAssetAsync(string key, Type assetType, CancellationToken ct = default)
        {
            await UniTask.Yield(ct);
            if (!IsAvailable())
            {
                UnityEngine.Debug.LogWarning("AddressablesBackend: Addressables package not found, load skipped.");
                return null;
            }

            // 反射兜底：避免在未安装 Addressables 时编译失败。
            // 可在后续接入中替换为强类型 AsyncOperationHandle<T> 调用。
            try
            {
                MethodInfo loadMethod = _addressablesType.GetMethod("LoadAssetAsync", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(object), typeof(Type) }, null);
                if (loadMethod == null) return null;
                var handle = loadMethod.Invoke(null, new object[] { key, assetType });
                if (handle == null) return null;

                var handleType = handle.GetType();
                var resultProp = handleType.GetProperty("Result");
                if (resultProp == null) return null;
                return resultProp.GetValue(handle) as UnityEngine.Object;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"AddressablesBackend.LoadAssetAsync failed: {e.Message}");
                return null;
            }
        }

        public async UniTask<IReadOnlyList<TextAsset>> LoadTextAssetsByGroupAsync(string groupKey, CancellationToken ct = default)
        {
            await UniTask.Yield(ct);
            return Array.Empty<TextAsset>();
        }

        public UniTask PreloadAsync(IEnumerable<string> keys, CancellationToken ct = default)
        {
            return UniTask.CompletedTask;
        }

        public void Release(string key, UnityEngine.Object loadedAsset)
        {
            // Addressables 正式接入时应在此调用 Release(handle/object)。
        }
    }
}
