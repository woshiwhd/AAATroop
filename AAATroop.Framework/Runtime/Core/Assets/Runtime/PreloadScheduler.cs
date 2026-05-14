using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Script.Core.Assets
{
    public static class PreloadScheduler
    {
        public static async UniTask PreloadKeysAsync(IAssetService service, IEnumerable<string> keys, CancellationToken ct = default)
        {
            if (service == null || keys == null) return;
            foreach (var key in keys)
            {
                if (string.IsNullOrEmpty(key)) continue;
                await service.LoadAssetAsync<UnityEngine.Object>(key, ct);
            }
        }
    }
}
