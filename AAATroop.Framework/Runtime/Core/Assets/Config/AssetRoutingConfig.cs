using System;
using System.Collections.Generic;
using UnityEngine;

namespace Script.Core.Assets
{
    [CreateAssetMenu(fileName = "AssetRoutingConfig", menuName = "AAATroop/Assets/AssetRoutingConfig")]
    public class AssetRoutingConfig : ScriptableObject
    {
        [Serializable]
        public class PrefixRoute
        {
            public string keyPrefix;
            public AssetBackendType backendType = AssetBackendType.Resources;
        }

        [Serializable]
        public class GroupRoute
        {
            public string groupKey;
            public AssetBackendType backendType = AssetBackendType.Resources;
        }

        public AssetBackendType defaultBackend = AssetBackendType.Resources;
        public string yooAssetPackageName = "DefaultPackage";
        [Tooltip("开启后仅允许 YooAsset 后端加载资源，不再回退 Resources。")]
        public bool yooAssetOnly = false;
        public List<PrefixRoute> prefixRoutes = new List<PrefixRoute>();
        public List<GroupRoute> groupRoutes = new List<GroupRoute>();
    }
}
