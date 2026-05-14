using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Script.Utilities;

namespace Script.Core.Assets
{
    [DefaultExecutionOrder(-900)]
    public class AssetRuntimeBootstrap : MonoBehaviour
    {
        [SerializeField] private AssetRoutingConfig routingConfig;
        [SerializeField] private AssetGroupsConfig groupsConfig;
        [SerializeField] private bool dontDestroyOnLoad = true;

        async void Awake()
        {
            if (AssetServiceLocator.Current != null)
            {
                if (dontDestroyOnLoad) DontDestroyOnLoad(gameObject);
                return;
            }

            var backends = new Dictionary<AssetBackendType, IAssetBackend>
            {
                { AssetBackendType.Resources, new ResourcesBackend() },
                { AssetBackendType.YooAsset, new YooAssetBackend(routingConfig != null ? routingConfig.yooAssetPackageName : "DefaultPackage") },
                { AssetBackendType.Addressables, new AddressablesBackend() }
            };

            var service = new AssetService(backends, routingConfig, groupsConfig);
            AssetServiceLocator.Set(service);
            await service.InitializeAsync();
            GameLog.Log("AssetRuntimeBootstrap: AssetService initialized.");

            if (dontDestroyOnLoad) DontDestroyOnLoad(gameObject);
        }
    }
}
