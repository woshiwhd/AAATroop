using System;
using UnityEngine;
using YooAsset;

namespace Script.Core.Assets
{
    /// <summary>
    /// 资源定位策略：将“业务命名”映射为 YooAsset location，避免规则散落在业务代码。
    /// </summary>
    public sealed class AssetLocationStrategy
    {
        private const string SceneRoot = "Assets/GameAssets/Scene";
        private const string UIRoot = "Assets/GameAssets/UI";
        private const string UnitRoot = "Assets/GameAssets/Units";
        private const string AudioRoot = "Assets/GameAssets/Audio";
        private const string MapRoot = "Assets/GameAssets/Map";

        public string ResolveSceneLocation(ResourcePackage package, string sceneNameInput)
        {
            string sceneName = ExtractName(sceneNameInput);
            if (string.IsNullOrWhiteSpace(sceneName))
                return null;

            string conventionLocation = $"{SceneRoot}/{sceneName}.scene";
            if (package != null && package.CheckLocationValid(conventionLocation))
                return conventionLocation;
            if (package != null && package.CheckLocationValid(sceneName))
                return sceneName;
            return conventionLocation;
        }

        public string ResolveAssetLocation(ResourcePackage package, string key, Type assetType)
        {
            if (string.IsNullOrWhiteSpace(key))
                return key;

            string trimmed = key.Trim();
            if (trimmed.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return trimmed;

            if (!TryParseConventionPrefix(trimmed, out var prefix, out var namePart))
                return trimmed;

            string location = BuildConventionLocation(prefix, namePart, assetType);
            if (string.IsNullOrEmpty(location))
                return trimmed;

            if (package == null || package.CheckLocationValid(location))
                return location;
            return trimmed;
        }

        private static bool TryParseConventionPrefix(string key, out string prefix, out string namePart)
        {
            int idx = key.IndexOf(':');
            if (idx <= 0 || idx >= key.Length - 1)
            {
                prefix = null;
                namePart = null;
                return false;
            }

            prefix = key.Substring(0, idx).Trim().ToLowerInvariant();
            namePart = key.Substring(idx + 1).Trim();
            return !string.IsNullOrWhiteSpace(namePart);
        }

        private static string BuildConventionLocation(string prefix, string namePart, Type assetType)
        {
            string ext = GetDefaultExtension(assetType);
            return prefix switch
            {
                "scene" => $"{SceneRoot}/{ExtractName(namePart)}.scene",
                "ui" => $"{UIRoot}/{NormalizeAssetName(namePart, ext)}",
                "unit" => $"{UnitRoot}/{NormalizeAssetName(namePart, ext)}",
                "audio" => $"{AudioRoot}/{namePart}",
                "map" => $"{MapRoot}/{namePart}",
                _ => null
            };
        }

        private static string GetDefaultExtension(Type assetType)
        {
            if (assetType == typeof(GameObject))
                return ".prefab";
            if (assetType == typeof(SceneAssetMarker))
                return ".scene";
            return string.Empty;
        }

        private static string NormalizeAssetName(string namePart, string defaultExt)
        {
            if (string.IsNullOrWhiteSpace(namePart))
                return string.Empty;
            if (!string.IsNullOrEmpty(defaultExt) && !namePart.EndsWith(defaultExt, StringComparison.OrdinalIgnoreCase))
                return namePart + defaultExt;
            return namePart;
        }

        private static string ExtractName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            string value = raw.Trim();
            int slash = value.LastIndexOf('/');
            if (slash >= 0 && slash < value.Length - 1)
                value = value.Substring(slash + 1);
            int dot = value.LastIndexOf('.');
            if (dot > 0)
                value = value.Substring(0, dot);
            return value;
        }

        // 仅用于通过 Type 区分场景扩展名，避免引入 Editor 依赖。
        private sealed class SceneAssetMarker { }
    }
}
