using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Script
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

        // 已废弃：不再按坐标加载外部 chunk 文件，改为仅使用模板库随机填充，因此不需要 IResourceProvider。
        // private IResourceProvider _provider;

        [Header("Template Fallback")]
        [Tooltip("如果按坐标找不到 chunk 文件，是否从模板库随机选择一个模板填充当前 chunk")]
        [SerializeField] private bool useTemplateFallback = true;
        private ChunkTemplateLoader _templateLoader;

        void Awake()
        {
            if (targetTilemap == null) targetTilemap = GetComponent<Tilemap>();
            _cam = Camera.main;
            _lastCamPos = _cam ? _cam.transform.position : Vector3.zero;

            // 不再使用 ResourcesProvider（已切换为模板随机填充）
            // default provider code removed

            // 初始化模板加载器（仅在启用 fallback 时）
            if (useTemplateFallback)
            {
                _templateLoader = new ChunkTemplateLoader();
                _templateLoader.Initialize(chunkWidth, chunkHeight, enableDebugLogs);
            }

            // 自动连引用：若 Inspector 中没有设置 tileDatabase 或 tilemaps，则尝试从场景中查找
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
                    // 在编辑器中，尝试通过 AssetDatabase 查找任意 TileDatabase 资源并赋值
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

                    // 运行时尝试从 Resources 中加载任意 TileDatabase（如果开发者将其放到 Resources 下）
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
                // 尝试在场景中查找名为 "Grid" 的对象并获取其下第一个 Tilemap
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

            // initial refresh
            RefreshVisibleChunks().Forget();
        }

        void Update()
        {
            if (_cam == null) return;
            if (Vector3.Distance(_cam.transform.position, _lastCamPos) > cameraMoveThreshold)
            {
                _lastCamPos = _cam.transform.position;
                RefreshVisibleChunks().Forget();
            }
        }

        private async UniTask RefreshVisibleChunks()
        {
            // compute visible cell bounds
            float zDistance = Mathf.Abs(_cam.transform.position.z - targetTilemap.transform.position.z);
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

            // start loads for desired chunks：如果没有记录则创建加载任务
            foreach (var chunk in desired)
            {
                if (_records.ContainsKey(chunk)) continue;
                StartLoadChunk(chunk);
            }

            // unload chunks that are tracked but not desired
            if (unloadOutOfView)
            {
                var toUnload = new List<Vector2Int>();
                foreach (var kv in _records)
                {
                    if (!desired.Contains(kv.Key)) toUnload.Add(kv.Key);
                }

                foreach (var chunk in toUnload)
                {
                    UnloadChunk(chunk);
                }
            }

            await UniTask.Yield();
        }

        private void StartLoadChunk(Vector2Int chunk)
        {
            // 若已有记录并非 Unloading 则跳过；若为 Unloading，直接返回
            if (_records.TryGetValue(chunk, out var existRec))
            {
                if (existRec.state == ChunkStateEnum.Unloading) return;
                if (existRec.state == ChunkStateEnum.Loading || existRec.state == ChunkStateEnum.Loaded) return;
            }

            var cts = new CancellationTokenSource();
            var rec = new ChunkRecord { state = ChunkStateEnum.Loading, cts = cts, generation = (existRec != null ? existRec.generation + 1 : 1) };
            _records[chunk] = rec;

            // fire-and-forget loader
            LoadChunkInternal(chunk, rec.generation, cts.Token).Forget();
        }

        private async UniTask LoadChunkInternal(Vector2Int chunk, int generation, CancellationToken ct)
        {
            ChunkRecord rec;
            if (!_records.TryGetValue(chunk, out rec)) return;
            if (rec.generation != generation) return; // newer request superseded

            // 只使用模板模式：直接从模板库获取随机模板并把 origin 置为对应 chunk 的全局 origin
            Script.TilemapLoader.ChunkData data = null;
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
            catch (Exception ex)
            {
                Debug.LogError($"TilemapManager: exception while selecting template for chunk {chunk}: {ex}");
                return;
            }

            // 在写入之前再次检查当前记录与 generation，避免 stale write
            if (!_records.TryGetValue(chunk, out rec)) return;
            if (rec.generation != generation) return;
            if (rec.state != ChunkStateEnum.Loading) return;

            // 确保在主线程执行写入（Tilemap.SetTile 必须在主线程）
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
                        // 在写入过程中也再次检查是否被卸载/替换
                        if (!_records.TryGetValue(chunk, out rec)) return;
                        if (rec.generation != generation || rec.state != ChunkStateEnum.Loading) return;

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

                // 写入完成，标记为 Loaded
                if (_records.TryGetValue(chunk, out rec) && rec.generation == generation)
                {
                    // 保存用于调试/查询的 chunk 数据副本
                    try { rec.chunkData = data; } catch { }
                    rec.state = ChunkStateEnum.Loaded;
                }
            }
            catch (OperationCanceledException)
            {
                // 被取消，直接返回
            }
            catch (Exception ex)
            {
                Debug.LogError($"TilemapManager: exception while writing chunk {chunk}: {ex}");
            }
            finally
            {
                // 加载完成或取消后，若记录已存在且 CTS 已取消/完成，我们不自动 Dispose 这里，让卸载流程负责 dispose
            }
        }

        private void UnloadChunk(Vector2Int chunk)
        {
            // fire-and-forget 卸载流程
            UnloadChunkAsync(chunk).Forget();
        }

        private async UniTask UnloadChunkAsync(Vector2Int chunk)
        {
            if (!_records.TryGetValue(chunk, out var rec)) return;
            // 防止重复卸载
            if (rec.state == ChunkStateEnum.Unloading) return;
            rec.state = ChunkStateEnum.Unloading;

            try
            {
                // 取消正在进行的加载
                try { rec.cts?.Cancel(); } catch { }

                // 调用主线程清理
                await ClearChunkAsync(chunk, CancellationToken.None);
                if (enableDebugLogs) Debug.Log($"TilemapManager: Unloaded chunk {chunk}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"TilemapManager: UnloadChunkAsync error for {chunk}: {ex}");
            }
            finally
            {
                // 移除记录并释放资源
                if (_records.TryGetValue(chunk, out var r))
                {
                    try { r.cts?.Dispose(); } catch { }
                    _records.Remove(chunk);
                }
            }
        }

        /// <summary>
        /// 在主线程清除指定 chunk 的 Tile（并可等待完成）。
        /// 清理范围以 chunkWidth/chunkHeight 为单元，chunk 坐标为块索引。
        /// </summary>
        private async UniTask ClearChunkAsync(Vector2Int chunk, CancellationToken ct)
        {
            // 确保在主线程执行 Tilemap 操作
            await UniTask.SwitchToMainThread(ct);

            int ox = chunk.x * chunkWidth;
            int oy = chunk.y * chunkHeight;

            for (int y = 0; y < chunkHeight; y++)
            {
                for (int x = 0; x < chunkWidth; x++)
                {
                    ct.ThrowIfCancellationRequested();
                    Vector3Int cell = new Vector3Int(ox + x, oy + y, 0);
                    targetTilemap.SetTile(cell, null);
                    if (blockingTilemap != null) blockingTilemap.SetTile(cell, null);
                }
                // 每行后短暂让出，避免单帧太多操作（可调整或移除）
                await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate, ct);
            }
        }

        /// <summary>
        /// 接收外部期望的 chunk 集合并执行差分加载/卸载。
        /// 假定调用方在主线程（例如 ChunkManager.Update 调用）。
        /// </summary>
        public void SetDesiredChunks(HashSet<Vector2Int> desired)
        {
            if (desired == null) return;

            // current tracked (loading/loaded)
            var current = new HashSet<Vector2Int>(_records.Keys);

            // start loads for desired - current
            foreach (var d in desired)
            {
                if (current.Contains(d)) continue;
                StartLoadChunk(d);
                if (enableDebugLogs) Debug.Log($"TilemapManager: Request load {d}");
            }

            // unload for current - desired
            var toUnload = new List<Vector2Int>();
            foreach (var c in current)
            {
                if (!desired.Contains(c)) toUnload.Add(c);
            }
            foreach (var u in toUnload)
            {
                UnloadChunk(u);
                if (enableDebugLogs) Debug.Log($"TilemapManager: Request unload {u}");
            }
        }

        // public helper: force clear all tracked chunks and cancel loads (awaitable)
        public async UniTask ClearAllAsync()
        {
            var keys = new List<Vector2Int>(_records.Keys);
            var tasks = new List<UniTask>();
            foreach (var k in keys)
            {
                tasks.Add(UnloadChunkAsync(k));
            }
            await UniTask.WhenAll(tasks);
        }

        // backward-compatible wrapper (fire-and-forget)
        public void ClearAll()
        {
            ClearAllAsync().Forget();
        }

        void OnDestroy()
        {
            ClearAll();
        }

        /// <summary>
        /// 尝试获取已加载的 chunk 数据（仅在 chunk 已加载时返回 true）。编辑器可调用此方法来绘制调试信息。
        /// </summary>
        public bool TryGetChunkData(Vector2Int chunk, out TilemapLoader.ChunkData data)
        {
            data = null;
            if (_records.TryGetValue(chunk, out var rec))
            {
                if (rec != null && rec.chunkData != null && rec.state == ChunkStateEnum.Loaded)
                {
                    data = rec.chunkData;
                    return true;
                }
            }
            return false;
        }
    }
}
