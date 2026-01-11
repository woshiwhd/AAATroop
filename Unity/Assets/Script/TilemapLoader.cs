using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Script
{
    public static class TilemapLoader
    {
        // JSON schema for chunk
        [Serializable]
        public class ChunkData
        {
            public int originX;
            public int originY;
            public int width;
            public int height;
            public int[] tiles; // row-major width*height
            public byte[] blocking; // optional: 0/1 per cell
        }

        // 仅加载并解析 chunk 数据，返回 ChunkData 给调用方（不会写入 Tilemap）。
        // 注意：JsonUtility 属于 Unity API，解析在主线程执行。
        public static async UniTask<ChunkData> LoadChunkDataAsync(IResourceProvider provider, string resourcePath, CancellationToken ct = default)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));

            TextAsset ta = await provider.LoadTextAsync(resourcePath, ct);
            if (ta == null)
            {
                Debug.LogWarning($"TilemapLoader: resource not found at {resourcePath}");
                return null;
            }

            // 读取和解析 JSON 必须在主线程执行
            await UniTask.SwitchToMainThread(ct);
            string jsonText = ta.text;

            try
            {
                var chunk = JsonUtility.FromJson<ChunkData>(jsonText);
                return chunk;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"TilemapLoader: Json parse failed for {resourcePath}: {ex}");
                return null;
            }
        }
    }
}
