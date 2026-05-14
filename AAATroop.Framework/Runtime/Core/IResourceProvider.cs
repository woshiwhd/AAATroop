using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Script.Core
{
    /// <summary>
    /// 兼容旧接口：从资源路径异步加载 TextAsset。
    /// 新项目建议改用 Script.Core.Assets.IAssetService。
    /// </summary>
    public interface IResourceProvider
    {
        UniTask<TextAsset> LoadTextAsync(string path, CancellationToken ct = default);
    }
}
