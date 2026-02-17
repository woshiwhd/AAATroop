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
    /// TilemapManager: 按摄像机视野按块异步加载 Tile 数据（依赖 TilemapLoader）并维护已加载块集合。
    /// - 自动使用 ResourcesProvider 作为默认的 IResourceProvider（如果需要自定义 provider，可扩展）。
    /// - 支持取消正在加载的块以及视野外卸载。
    /// - chunkPathFormat 默认格式为 "chunks/chunk_{0}_{1}"（不含扩展名），可在 Inspector 中修改以匹配导出资源命名。
    /// 注意：假设每个块导出为一个 JSON 文本资源，使用 provider.LoadTextAsync(path) 来加载。
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
        [SerializeField] private int viewPaddingChunks = 1;
        [SerializeField] private int tilesPerFrame = 256;
        [SerializeField] private bool unloadOutOfView = true;

        [Header("Update Settings")]
        [SerializeField] private float cameraMoveThreshold = 0.1f; // world units to trigger update

        // 可在 Inspector 打开以输出调试日志
        [Header("Debug")]
        [SerializeField] private bool enableDebugLogs = false;

        // runtime
        private Camera _cam;
        private Vector3 _lastCamPos;
        private bool _refreshing;

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
            _cam = Camera.main;
            _lastCamPos = _cam ? _cam.transform.position : Vector3.zero;

            if (useTemplateFallback)
            {
                _templateLoader = new ChunkTemplateLoader();
                _templateLoader.Initialize(chunkWidth, chunkHeight, enableDebugLogs);
            }

            if (tileDatabase == null)
            {
                var foundDb = FindObjectOfType<TileDatabase>();
                if (foundDb != null)
                {
                    tileDatabase = foundDb;
                    if (enableDebugLogs) Debug.Log("TilemapManager: 自动找到 TileDatabase 并赋值。");
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
                                if (enableDebugLogs) Debug.Log($"TilemapManager: 在编辑器 AssetDatabase 中找到 TileDatabase 并赋值 ({path})");
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
                                if (enableDebugLogs) Debug.Log("TilemapManager: 从 Resources.LoadAll 中找到 TileDatabase 并赋值。");
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
                        if (enableDebugLogs) Debug.Log("TilemapManager: 在 Grid 下找到 Tilemap 并自动赋值给 targetTilemap。");
                    }
                }
            }
        }

        void Start()
        {
            if (targetTilemap == null)
            {
                Debug.LogError("TilemapManager: targetTilemap 未设置。");
                enabled = false;
                return;
            }
            if (tileDatabase == null)
            {
                Debug.LogError("TilemapManager: tileDatabase 未设置。");
                enabled = false;
                return;
            }

            RefreshVisibleChunks().Forget();
        }

        void Update()
        {
            if (_cam == null) return;
            if (_refreshing) return; // 避免多份并发导致遍历 _records 时被 UnloadChunk 修改而抛异常
            _refreshing = true;
            RefreshVisibleChunks().Forget();
        }

        private async UniTask RefreshVisibleChunks()
        {
            try
            {
                float zDistance = Math.Abs(_cam.transform.position.z - targetTilemap.transform.position.z);
                Vector3 bottomLeft = _cam.ScreenToWorldPoint(new Vector3(0f, 0f, zDistance));
                Vector3 topRight = _cam.ScreenToWorldPoint(new Vector3(Screen.width, Screen.height, zDistance));

                Vector3Int cellBL = targetTilemap.WorldToCell(bottomLeft);
                Vector3Int cellTR = targetTilemap.WorldToCell(topRight);

                int minChunkX = Mathf.FloorToInt((float)cellBL.x / chunkWidth) - viewPaddingChunks;
                int maxChunkX = Mathf.FloorToInt((float)cellTR.x / chunkWidth) + viewPaddingChunks;
                int minChunkY = Mathf.FloorToInt((float)cellBL.y / chunkHeight) - viewPaddingChunks;
                int maxChunkY = Mathf.FloorToInt((float)cellTR.y / chunkHeight) + viewPaddingChunks;

                var desired = new HashSet<Vector2Int>();
                for (int cx = minChunkX; cx <= maxChunkX; cx++)
                {
                    for (int cy = minChunkY; cy <= maxChunkY; cy++)
                    {
                        desired.Add(new Vector2Int(cx, cy));
                    }
                }

                foreach (var chunk in desired)
                {
                    if (_records.ContainsKey(chunk)) continue;
                    StartLoadChunk(chunk);
                }

                if (unloadOutOfView)
                {
                    var toUnload = new List<Vector2Int>();
                    var keysCopy = new List<Vector2Int>(_records.Keys); // 副本遍历，避免 UnloadChunk 修改 _records 时抛 InvalidOperationException
                    foreach (var k in keysCopy)
                    {
                        if (!desired.Contains(k)) toUnload.Add(k);
                    }
                    foreach (var chunk in toUnload)
                        UnloadChunk(chunk);
                }

                await UniTask.Yield();
            }
            finally
            {
                _refreshing = false;
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
                        if (enableDebugLogs) Debug.Log($"TilemapManager: 使用模板填充 chunk {chunk}");
                    }
                }

                if (data == null)
                {
                    if (enableDebugLogs) Debug.LogWarning($"TilemapManager: 未找到可用模板以填充 chunk {chunk}");
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
                Debug.LogError($"TilemapManager: 加载 chunk {chunk} 时发生异常：{e}");
            }
        }

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
