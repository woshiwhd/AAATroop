using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Script.Core.Assets
{
    public interface IAssetService
    {
        UniTask InitializeAsync(CancellationToken ct = default);

        UniTask<T> LoadAssetAsync<T>(string key, CancellationToken ct = default) where T : UnityEngine.Object;
        UniTask<IReadOnlyList<TextAsset>> LoadTextAssetsByGroupAsync(string groupKey, CancellationToken ct = default);
        UniTask PreloadGroupAsync(string groupKey, CancellationToken ct = default);

        UniTask<Scene> LoadSceneAsync(string sceneKey, LoadSceneMode mode = LoadSceneMode.Single, CancellationToken ct = default);
        void Release(string key, UnityEngine.Object loadedAsset);
    }
}
