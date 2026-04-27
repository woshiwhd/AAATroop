using System;
using System.Collections.Generic;
using UnityEngine;

namespace Script.Managers
{
    /// <summary>
    /// 地形段：主 Perlin 选段；段内从 TileDatabase 连续 id [baseTileId, baseTileId+grassVariantCount-1] 中用副 Perlin 选草地变体（可复现斑块）。
    /// </summary>
    [Serializable]
    public class TerrainNoiseBand
    {
        [Tooltip("主噪声下界 [0,1]，含")]
        public float noiseMin;
        [Tooltip("主噪声上界 [0,1]，不含")]
        public float noiseMax = 1f;
        [Tooltip("草地变体起始 TileDatabase id（常为 1）")]
        public int baseTileId = 1;
        [Tooltip("连续草地变体数量（8 即占用 baseTileId..baseTileId+7）")]
        [Min(1)] public int grassVariantCount = 8;
        [Tooltip("是否阻挡移动")]
        public bool blocking;
    }

    /// <summary>
    /// 主噪声选地形段；副噪声在段内选草地变体索引。
    /// </summary>
    public class ProceduralChunkGenerator : MonoBehaviour, IChunkGenerator
    {
        [Header("主噪声（选地形段）")]
        [SerializeField] private float noiseScale = 0.03f;

        [Header("副噪声（草地变体）")]
        [Tooltip("越小斑块越大，约 0.05~0.15")]
        [SerializeField] private float variantNoiseScale = 0.2f;
        [SerializeField, Min(1)] private int logVariantNoiseMaxCount = 80;
        private int _variantNoiseLogCount;

        [Header("地形段")]
        [SerializeField] private List<TerrainNoiseBand> terrainBands = new List<TerrainNoiseBand>
        {
            new TerrainNoiseBand
            {
                noiseMin = 0f,
                noiseMax = 1f,
                baseTileId = 1,
                grassVariantCount = 8,
                blocking = false
            }
        };

        [Header("Ground")]
        [SerializeField] private bool useGroundLayer = true;

        public TilemapLoader.ChunkData GenerateChunk(int worldSeed, Vector2Int chunkCoord, int chunkWidth, int chunkHeight)
        {
            _variantNoiseLogCount = 0;

            // 避免使用超大 seed 直接乘常数导致 float 精度丢失（相邻格 +1 的差异被吞掉）
            float offX = BuildSeedOffset(worldSeed, 0x9E3779B9u);
            float offY = BuildSeedOffset(worldSeed, 0x85EBCA6Bu);
            float vOffX = BuildSeedOffset(worldSeed, 0xC2B2AE35u);
            float vOffY = BuildSeedOffset(worldSeed, 0x27D4EB2Fu);

            IReadOnlyList<TerrainNoiseBand> bands = EffectiveBands();

            int len = chunkWidth * chunkHeight;
            var tiles = new int[len];
            var blocking = new byte[len];
            var ground = useGroundLayer ? new int[len] : null;

            int originX = chunkCoord.x * chunkWidth;
            int originY = chunkCoord.y * chunkHeight;

            for (int ly = 0; ly < chunkHeight; ly++)
            {
                for (int lx = 0; lx < chunkWidth; lx++)
                {
                    int worldX = originX + lx;
                    int worldY = originY + ly;
                    float n = Mathf.PerlinNoise((worldX + offX) * noiseScale, (worldY + offY) * noiseScale);

                    TerrainNoiseBand band = PickBand(bands, n);
                    int tid = PickGrassVariantTileId(worldX, worldY, band, vOffX, vOffY);
                    byte blk = band.blocking ? (byte)1 : (byte)0;

                    int idx = ly * chunkWidth + lx;
                    tiles[idx] = tid;
                    blocking[idx] = blk;
                    if (ground != null) ground[idx] = tid;
                }
            }

            return new TilemapLoader.ChunkData
            {
                width = chunkWidth,
                height = chunkHeight,
                originX = originX,
                originY = originY,
                tiles = tiles,
                blocking = blocking,
                ground = ground,
                templateName = null
            };
        }

        private IReadOnlyList<TerrainNoiseBand> EffectiveBands()
        {
            if (terrainBands != null && terrainBands.Count > 0) return terrainBands;
            return new[]
            {
                new TerrainNoiseBand
                {
                    noiseMin = 0f,
                    noiseMax = 1f,
                    baseTileId = 1,
                    grassVariantCount = 8
                }
            };
        }

        private static TerrainNoiseBand PickBand(IReadOnlyList<TerrainNoiseBand> bands, float n)
        {
            for (int i = 0; i < bands.Count; i++)
            {
                var b = bands[i];
                if (n >= b.noiseMin && n < b.noiseMax) return b;
            }

            return bands[bands.Count - 1];
        }

        private int PickGrassVariantTileId(int worldX, int worldY, TerrainNoiseBand b, float vOffX, float vOffY)
        {
            int count = Mathf.Max(1, b.grassVariantCount);
            if (count == 1) return b.baseTileId;

            // 单层 Perlin 在相邻格上变化可能过小；叠加一层高频细节提升局部差异。
            float s = Mathf.Max(0.0001f, variantNoiseScale);
            float vBase = Mathf.PerlinNoise((worldX + vOffX) * s, (worldY + vOffY) * s);
            float vDetail = Mathf.PerlinNoise((worldX - vOffY) * (s * 3.7f), (worldY + vOffX) * (s * 3.7f));
            float v = Mathf.Clamp01(vBase * 0.55f + vDetail * 0.45f);
            int index = Mathf.Clamp((int)(v * count), 0, count - 1);

            if (_variantNoiseLogCount < logVariantNoiseMaxCount)
            {
                _variantNoiseLogCount++;
                UnityEngine.Debug.Log(
                    $"GrassNoise #{_variantNoiseLogCount}: wx={worldX}, wy={worldY}, " +
                    $"baseId={b.baseTileId}, count={count}, scale={s:F4}, " +
                    $"vBase={vBase:F4}, vDetail={vDetail:F4}, v={v:F4}, index={index}, tileId={b.baseTileId + index}");
            }

            return b.baseTileId + index;
        }

        private static float BuildSeedOffset(int seed, uint salt)
        {
            // 把 seed 映射到 [0, 10000) 的可复现偏移，既能区分不同种子，又不会引发浮点精度问题
            uint x = (uint)seed ^ salt;
            x ^= x >> 16;
            x *= 0x7FEB352Du;
            x ^= x >> 15;
            x *= 0x846CA68Bu;
            x ^= x >> 16;

            float unit = (x & 0x00FFFFFFu) / 16777215f; // 24-bit -> [0,1]
            return unit * 10000f;
        }
    }
}
