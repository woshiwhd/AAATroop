using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Tilemaps;
using Script.Utilities;

namespace Script.Managers
{
    /// <summary>
    /// TilemapManager：地图系统的“加载/写入层”。
    /// - 仅通过 ChunkManager 传入的 desired chunks（SetDesiredChunks）驱动加载/卸载
    /// - 负责：异步加载 ChunkData（TilemapLoader/模板回退）并写入 Tilemap；视野外卸载并清瓦片
    /// </summary>
    public class TilemapManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Tilemap targetTilemap;
        [SerializeField] private Tilemap blockingTilemap;
        [SerializeField] private TileDatabase tileDatabase;

        [Header("Chunk Settings")]
        [SerializeField] private int chunkWidth = 32;
        [SerializeField] private int chunkHeight = 32;
        [SerializeField] private int tilesPerFrame = 256;
        [SerializeField] private bool unloadOutOfView = true;

        // chunk 状态记录：统一管理每个 chunk 的 state/cts/generation
        private enum ChunkStateEnum { Loading, Loaded, Unloading }
        private class ChunkRecord
        {
            public ChunkStateEnum state;
            public CancellationTokenSource cts;
            public int generation;
            // 保存已加载的 ChunkData，便于编辑器调试展示（仅存内存）
            public TilemapLoader.ChunkData chunkData;
        }
        // 记录所有 chunk 的状态；键为 chunk 坐标
        private readonly Dictionary<Vector2Int, ChunkRecord> _records = new Dictionary<Vector2Int, ChunkRecord>();

        [Header("Template Fallback")]
        [Tooltip("如果按坐标找不到 chunk 文件，是否从模板库随机选择一个模板填充当前 chunk")]
        [SerializeField] private bool useTemplateFallback = true;
        private ChunkTemplateLoader _templateLoader;

        void Awake()
        {
            if (targetTilemap == null) targetTilemap = GetComponent<Tilemap>();

            if (useTemplateFallback)
            {
                _templateLoader = new ChunkTemplateLoader();
                _templateLoader.Initialize(chunkWidth, chunkHeight, false);
            }

            if (tileDatabase == null)
            {
                var foundDb = FindObjectOfType<TileDatabase>();
                if (foundDb != null)
                {
                    tileDatabase = foundDb;
                    GameLog.Log("TilemapManager: 自动找到 TileDatabase 并赋值。");
                }
                else
                {
#if UNITY_EDITOR
                    try
                    {
                        var guids = UnityEditor.AssetDatabase.FindAssets("t:TileDatabase");
                        if (guids != null && guids.Length > 0)
                        {
                            var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                            var dbAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<TileDatabase>(path);
                            if (dbAsset != null)
                            {
                                tileDatabase = dbAsset;
                                GameLog.Log($"TilemapManager: 在编辑器 AssetDatabase 中找到 TileDatabase 并赋值 ({path})");
                            }
                        }
                    }
                    catch { }
#endif

                    if (tileDatabase == null)
                    {
                        try
                        {
                            var arr = Resources.LoadAll<TileDatabase>("");
                            if (arr != null && arr.Length > 0)
                            {
                                tileDatabase = arr[0];
                                GameLog.Log("TilemapManager: 从 Resources.LoadAll 中找到 TileDatabase 并赋值。");
                            }
                        }
                        catch { }
                    }
                }
            }

            if (targetTilemap == null)
            {
                var grid = GameObject.Find("Grid");
                if (grid != null)
                {
                    var tm = grid.GetComponentInChildren<Tilemap>();
                    if (tm != null)
                    {
                        targetTilemap = tm;
                        GameLog.Log("TilemapManager: 在 Grid 下找到 Tilemap 并自动赋值给 targetTilemap。");
                    }
                }
            }
        }

        void Start()
        {
            if (targetTilemap == null)
            {
                GameLog.LogError("TilemapManager: targetTilemap 未设置。");
                enabled = false;
                return;
            }
            if (tileDatabase == null)
            {
                GameLog.LogError("TilemapManager: tileDatabase 未设置。");
                enabled = false;
                return;
            }
        }

        /// <summary>
        /// 由 ChunkManager 调用：将期望加载的 chunk 列表交给 TilemapManager 执行加载/卸载。
        /// </summary>
        public void SetDesiredChunks(IEnumerable<Vector2Int> desired)
        {
            var desiredSet = desired as HashSet<Vector2Int> ?? new HashSet<Vector2Int>(desired);
            foreach (var chunk in desiredSet)
            {
                if (_records.ContainsKey(chunk)) continue;
                StartLoadChunk(chunk);
            }
            if (unloadOutOfView)
            {
                var toUnload = new List<Vector2Int>();
                var keysCopy = new List<Vector2Int>(_records.Keys);
                foreach (var k in keysCopy)
                {
                    if (!desiredSet.Contains(k)) toUnload.Add(k);
                }
                foreach (var chunk in toUnload)
                    UnloadChunk(chunk);
            }
        }

        private void StartLoadChunk(Vector2Int chunk)
        {
            if (_records.TryGetValue(chunk, out var existRec))
            {
                if (existRec.state == ChunkStateEnum.Unloading) return;
                if (existRec.state == ChunkStateEnum.Loading || existRec.state == ChunkStateEnum.Loaded) return;
            }

            var cts = new CancellationTokenSource();
            var rec = new ChunkRecord { state = ChunkStateEnum.Loading, cts = cts, generation = (existRec != null ? existRec.generation + 1 : 1) };
            _records[chunk] = rec;

            LoadChunkInternal(chunk, rec.generation, cts.Token).Forget();
        }

        private async UniTask LoadChunkInternal(Vector2Int chunk, int generation, CancellationToken ct)
        {
            ChunkRecord rec;
            if (!_records.TryGetValue(chunk, out rec)) return;
            if (rec.generation != generation) return;

            TilemapLoader.ChunkData data = null;
            try
            {
                if (useTemplateFallback && _templateLoader != null)
                {
                    var tpl = _templateLoader.GetRandomTemplateCopy();
                    if (tpl != null)
                    {
                        tpl.originX = chunk.x * chunkWidth;
                        tpl.originY = chunk.y * chunkHeight;
                        data = tpl;
                        GameLog.Log($"TilemapManager: 使用模板填充 chunk {chunk}");
                    }
                }

                if (data == null)
                {
                    GameLog.LogWarning($"TilemapManager: 未找到可用模板以填充 chunk {chunk}");
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!_records.TryGetValue(chunk, out rec)) return;
            if (rec.generation != generation) return;
            if (rec.state != ChunkStateEnum.Loading) return;

            await UniTask.SwitchToMainThread(ct);

            try
            {
                int w = data.width;
                int h = data.height;
                int count = 0;
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        ct.ThrowIfCancellationRequested();
                        int idx = y * w + x;
                        int id = 0;
                        if (data.tiles != null && idx < data.tiles.Length) id = data.tiles[idx];

                        TileBase tile = tileDatabase.GetTileById(id);
                        Vector3Int cell = new Vector3Int(data.originX + x, data.originY + y, 0);
                        targetTilemap.SetTile(cell, tile);

                        if (blockingTilemap != null && data.blocking != null && idx < data.blocking.Length)
                        {
                            byte b = data.blocking[idx];
                            if (b != 0) blockingTilemap.SetTile(cell, tile);
                            else blockingTilemap.SetTile(cell, null);
                        }

                        count++;
                        if (count % tilesPerFrame == 0)
                        {
                            await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate, ct);
                        }
                    }
                }

                if (_records.TryGetValue(chunk, out rec) && rec.generation == generation)
                {
                    try { rec.chunkData = data; } catch { }
                    rec.state = ChunkStateEnum.Loaded;
                }
            }
            catch (OperationCanceledException)
            {
                // 被取消，直接返回
                return;
            }
            catch (Exception e)
            {
                GameLog.LogError($"TilemapManager: 加载 chunk {chunk} 时发生异常：{e}");
            }
        }

#if UNITY_EDITOR
        /// <summary>供 Scene 调试绘制使用：返回已加载的 chunk 坐标。</summary>
        public System.Collections.Generic.IEnumerable<Vector2Int> DebugGetLoadedChunkCoords()
        {
            if (_records == null) yield break;
            foreach (var kv in _records)
                if (kv.Value != null && kv.Value.state == ChunkStateEnum.Loaded)
                    yield return kv.Key;
        }

        public Tilemap DebugGetTilemap() => targetTilemap;
        public int DebugGetChunkWidth() => chunkWidth;
        public int DebugGetChunkHeight() => chunkHeight;
        public Camera DebugGetCamera() => Camera.main;
#endif

        /// <summary>
        /// 供编辑器调试用：尝试获取已加载 chunk 的 ChunkData（若未加载或正在加载则返回 false）。
        /// </summary>
        public bool TryGetChunkData(Vector2Int chunk, out TilemapLoader.ChunkData data)
        {
            data = null;
            if (_records.TryGetValue(chunk, out var rec) && rec != null && rec.state == ChunkStateEnum.Loaded)
            {
                data = rec.chunkData;
                return data != null;
            }
            return false;
        }

        // 用于 UnloadChunk 的清空数组（null 数组，SetTilesBlock 需传此类数组才能正确清空瓦片）
        private TileBase[] _clearTilesBuffer;

        private void UnloadChunk(Vector2Int chunk)
        {
            if (!_records.TryGetValue(chunk, out var rec)) return;

            if (rec.state == ChunkStateEnum.Loading && rec.cts != null)
                rec.cts.Cancel();

            int count = chunkWidth * chunkHeight;
            if (_clearTilesBuffer == null || _clearTilesBuffer.Length < count)
                _clearTilesBuffer = new TileBase[count];

            var bounds = new BoundsInt(chunk.x * chunkWidth, chunk.y * chunkHeight, 0, chunkWidth, chunkHeight, 1);
            if (targetTilemap != null)
                targetTilemap.SetTilesBlock(bounds, _clearTilesBuffer);
            if (blockingTilemap != null)
                blockingTilemap.SetTilesBlock(bounds, _clearTilesBuffer);

            _records.Remove(chunk);
        }
    }
}
