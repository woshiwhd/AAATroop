using UnityEngine;

namespace Script.Managers
{
    /// <summary>
    /// 按世界种子与 chunk 坐标确定性生成 ChunkData（与「属于哪个 chunk」无关的格点函数在实现内部用 world 坐标保证边界连贯）。
    /// </summary>
    public interface IChunkGenerator
    {
        TilemapLoader.ChunkData GenerateChunk(int worldSeed, Vector2Int chunkCoord, int chunkWidth, int chunkHeight);
    }
}
