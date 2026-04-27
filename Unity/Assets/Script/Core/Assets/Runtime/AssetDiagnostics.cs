using Script.Utilities;
using UnityEngine;

namespace Script.Core.Assets
{
    public static class AssetDiagnostics
    {
        public static void DumpMetrics()
        {
            var service = AssetServiceLocator.Current as AssetService;
            if (service == null)
            {
                GameLog.LogWarning("AssetDiagnostics: AssetService unavailable.");
                return;
            }

            GameLog.Log($"AssetDiagnostics: {service.Metrics}");
        }
    }
}
