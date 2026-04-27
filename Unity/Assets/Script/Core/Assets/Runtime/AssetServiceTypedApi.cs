using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.SceneManagement;

namespace Script.Core.Assets
{
    public static class AssetServiceTypedApi
    {
        public static UniTask<TextAsset> LoadTextAsync(this IAssetService service, string key, CancellationToken ct = default)
            => service.LoadAssetAsync<TextAsset>(key, ct);

        public static UniTask<TileBase> LoadTileAsync(this IAssetService service, string key, CancellationToken ct = default)
            => service.LoadAssetAsync<TileBase>(key, ct);

        public static UniTask<Sprite> LoadSpriteAsync(this IAssetService service, string key, CancellationToken ct = default)
            => service.LoadAssetAsync<Sprite>(key, ct);

        public static UniTask<GameObject> LoadPrefabAsync(this IAssetService service, string key, CancellationToken ct = default)
            => service.LoadAssetAsync<GameObject>(key, ct);

        public static UniTask<AudioClip> LoadAudioAsync(this IAssetService service, string key, CancellationToken ct = default)
            => service.LoadAssetAsync<AudioClip>(key, ct);

        public static UniTask<Scene> LoadSceneByKeyAsync(this IAssetService service, string sceneKey, LoadSceneMode mode = LoadSceneMode.Single, CancellationToken ct = default)
            => service.LoadSceneAsync(sceneKey, mode, ct);
    }
}
