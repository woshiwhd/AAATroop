using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using Script.Utilities;
using YooAsset;

namespace Script.Core.Assets
{
    public class AssetService : IAssetService
    {
        private readonly Dictionary<AssetBackendType, IAssetBackend> _backends;
        private readonly BackendResolver _resolver;
        private readonly AssetGroupsConfig _groupsConfig;
        private readonly AssetRoutingConfig _routingConfig;
        private readonly AssetCache _cache = new AssetCache();
        private readonly AssetPolicies _policies;
        private readonly AssetMetrics _metrics = new AssetMetrics();
        private readonly AssetLocationStrategy _locationStrategy = new AssetLocationStrategy();
        private bool _initialized;

        public AssetMetrics Metrics => _metrics;

        public AssetService(
            Dictionary<AssetBackendType, IAssetBackend> backends,
            AssetRoutingConfig routingConfig,
            AssetGroupsConfig groupsConfig,
            AssetPolicies policies = null)
        {
            _backends = backends ?? new Dictionary<AssetBackendType, IAssetBackend>();
            _routingConfig = routingConfig;
            _resolver = new BackendResolver(routingConfig);
            _groupsConfig = groupsConfig;
            _policies = policies ?? new AssetPolicies();
        }

        public async UniTask InitializeAsync(CancellationToken ct = default)
        {
            if (_initialized) return;
            foreach (var kv in _backends)
            {
                if (kv.Value == null) continue;
                await kv.Value.InitializeAsync(ct);
            }
            _initialized = true;
        }

        public async UniTask<T> LoadAssetAsync<T>(string key, CancellationToken ct = default) where T : UnityEngine.Object
        {
            _metrics.loadRequests++;
            if (string.IsNullOrEmpty(key)) return null;

            if (_cache.TryRetain(key, out var cached))
            {
                _metrics.cacheHits++;
                return cached as T;
            }

            bool yooOnly = _routingConfig != null && _routingConfig.yooAssetOnly;
            var backendType = yooOnly ? AssetBackendType.YooAsset : _resolver.ResolveBackendByKey(key);
            if (!_backends.TryGetValue(backendType, out var backend) || backend == null)
            {
                _metrics.loadFailures++;
                return null;
            }

            if (!backend.IsAvailable())
            {
                // 严格 Yoo 模式不回退。
                if (!yooOnly &&
                    backendType != AssetBackendType.Resources &&
                    _backends.TryGetValue(AssetBackendType.Resources, out var fallback) &&
                    fallback != null &&
                    fallback.IsAvailable())
                {
                    backend = fallback;
                    backendType = AssetBackendType.Resources;
                }
                else
                {
                    _metrics.loadFailures++;
                    return null;
                }
            }

            string resolvedKey = key;
            if (backendType == AssetBackendType.YooAsset)
            {
                resolvedKey = ResolveAssetKeyForYoo(key, typeof(T));
            }

            UnityEngine.Object loaded = null;
            int attempts = Math.Max(1, _policies.maxRetryCount + 1);
            for (int i = 0; i < attempts; i++)
            {
                loaded = await backend.LoadAssetAsync(resolvedKey, typeof(T), ct);
                if (loaded != null) break;
                if (i + 1 < attempts)
                {
                    _metrics.retries++;
                    await UniTask.Delay(_policies.retryDelayMs, cancellationToken: ct);
                }
            }

            if (loaded == null)
            {
                _metrics.loadFailures++;
                return null;
            }

            _cache.AddOrRetain(key, loaded, backendType);
            return loaded as T;
        }

        public async UniTask<IReadOnlyList<TextAsset>> LoadTextAssetsByGroupAsync(string groupKey, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(groupKey)) return Array.Empty<TextAsset>();

            // 优先使用配置中显式 key 列表，便于多后端统一
            if (_groupsConfig != null && _groupsConfig.TryGetGroup(groupKey, out var g) && g != null && g.assetKeys != null && g.assetKeys.Count > 0)
            {
                var list = new List<TextAsset>(g.assetKeys.Count);
                for (int i = 0; i < g.assetKeys.Count; i++)
                {
                    var ta = await LoadAssetAsync<TextAsset>(g.assetKeys[i], ct);
                    if (ta != null) list.Add(ta);
                }
                return list;
            }

            // 无配置时按 group 路由给后端处理（Resources 可直接按路径 LoadAll）
            bool yooOnly = _routingConfig != null && _routingConfig.yooAssetOnly;
            var backendType = yooOnly ? AssetBackendType.YooAsset : _resolver.ResolveBackendByGroup(groupKey);
            if (!_backends.TryGetValue(backendType, out var backend) || backend == null)
                return Array.Empty<TextAsset>();

            if (!backend.IsAvailable()) return Array.Empty<TextAsset>();
            return await backend.LoadTextAssetsByGroupAsync(groupKey, ct);
        }

        public async UniTask PreloadGroupAsync(string groupKey, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(groupKey)) return;
            if (_groupsConfig == null || !_groupsConfig.TryGetGroup(groupKey, out var g) || g == null || g.assetKeys == null) return;
            for (int i = 0; i < g.assetKeys.Count; i++)
            {
                await LoadAssetAsync<UnityEngine.Object>(g.assetKeys[i], ct);
            }
        }

        public async UniTask<Scene> LoadSceneAsync(string sceneKey, LoadSceneMode mode = LoadSceneMode.Single, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(sceneKey)) return default;
            bool yooOnly = _routingConfig != null && _routingConfig.yooAssetOnly;
            if (yooOnly)
            {
                var loaded = await TryLoadSceneByYooAsync(sceneKey, mode, ct);
                if (loaded.IsValid()) return loaded;
                return default;
            }

            var op = SceneManager.LoadSceneAsync(sceneKey, mode);
            if (op == null) return default;
            await op.ToUniTask(cancellationToken: ct);
            return SceneManager.GetSceneByName(sceneKey);
        }

        private async UniTask<Scene> TryLoadSceneByYooAsync(string sceneKey, LoadSceneMode mode, CancellationToken ct)
        {
            if (!YooAssets.Initialized)
            {
                GameLog.LogError("AssetService: YooAssets 尚未初始化，无法通过 YooAsset 加载场景。");
                return default;
            }

            string packageName = (_routingConfig != null && !string.IsNullOrWhiteSpace(_routingConfig.yooAssetPackageName))
                ? _routingConfig.yooAssetPackageName
                : "Main";
            var package = YooAssets.TryGetPackage(packageName);
            if (package == null)
            {
                GameLog.LogError($"AssetService: YooAsset package not found: {packageName}");
                return default;
            }

            string resolvedSceneKey = _locationStrategy.ResolveSceneLocation(package, sceneKey);
            if (string.IsNullOrEmpty(resolvedSceneKey))
            {
                GameLog.LogError($"AssetService: 场景名无效，未在目录 Assets/GameAssets/Scene 下找到。scene={sceneKey}");
                return default;
            }

            SceneHandle handle = package.LoadSceneAsync(resolvedSceneKey, mode);
            while (!handle.IsDone)
            {
                ct.ThrowIfCancellationRequested();
                await UniTask.Yield(ct);
            }

            if (handle.Status == EOperationStatus.Succeed)
            {
                var scene = handle.SceneObject;
                if (scene.IsValid())
                    return scene;
            }
            else
            {
                GameLog.LogError($"AssetService: YooAsset 场景加载失败 key={sceneKey}, resolved={resolvedSceneKey}, error={handle.LastError}");
            }

            return SceneManager.GetSceneByName(GetSceneNameForUnityQuery(sceneKey));
        }

        private string ResolveAssetKeyForYoo(string rawKey, Type assetType)
        {
            string packageName = (_routingConfig != null && !string.IsNullOrWhiteSpace(_routingConfig.yooAssetPackageName))
                ? _routingConfig.yooAssetPackageName
                : "Main";

            var package = YooAssets.TryGetPackage(packageName);
            string resolved = _locationStrategy.ResolveAssetLocation(package, rawKey, assetType);
            return string.IsNullOrWhiteSpace(resolved) ? rawKey : resolved;
        }

        private static string GetSceneNameForUnityQuery(string sceneKey)
        {
            string sceneName = sceneKey;
            int slash = sceneName.LastIndexOf('/');
            if (slash >= 0 && slash < sceneName.Length - 1)
                sceneName = sceneName.Substring(slash + 1);
            int dot = sceneName.LastIndexOf('.');
            if (dot > 0)
                sceneName = sceneName.Substring(0, dot);
            return sceneName;
        }

        public void Release(string key, UnityEngine.Object loadedAsset)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (_cache.Release(key, out var asset, out var backendType))
            {
                if (_backends.TryGetValue(backendType, out var backend) && backend != null)
                    backend.Release(key, asset);
            }
        }
    }
}
