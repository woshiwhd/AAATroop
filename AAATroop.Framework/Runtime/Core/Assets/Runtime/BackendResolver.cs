using System.Collections.Generic;

namespace Script.Core.Assets
{
    public class BackendResolver
    {
        private readonly AssetRoutingConfig _routingConfig;

        public BackendResolver(AssetRoutingConfig routingConfig)
        {
            _routingConfig = routingConfig;
        }

        public AssetBackendType ResolveBackendByKey(string key)
        {
            if (_routingConfig == null || string.IsNullOrEmpty(key)) return AssetBackendType.Resources;
            var routes = _routingConfig.prefixRoutes;
            if (routes != null)
            {
                for (int i = 0; i < routes.Count; i++)
                {
                    var route = routes[i];
                    if (route == null || string.IsNullOrEmpty(route.keyPrefix)) continue;
                    if (key.StartsWith(route.keyPrefix))
                        return route.backendType;
                }
            }
            return _routingConfig.defaultBackend;
        }

        public AssetBackendType ResolveBackendByGroup(string groupKey)
        {
            if (_routingConfig == null || string.IsNullOrEmpty(groupKey)) return AssetBackendType.Resources;
            List<AssetRoutingConfig.GroupRoute> routes = _routingConfig.groupRoutes;
            if (routes != null)
            {
                for (int i = 0; i < routes.Count; i++)
                {
                    var route = routes[i];
                    if (route == null || string.IsNullOrEmpty(route.groupKey)) continue;
                    if (route.groupKey == groupKey) return route.backendType;
                }
            }
            return _routingConfig.defaultBackend;
        }
    }
}
