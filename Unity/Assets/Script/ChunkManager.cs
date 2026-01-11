using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Script
{
    // ChunkManager：按块（chunk）加载/卸载 Tilemap 的管理器
    // 说明：
    // - 将地图切分为固定大小的块（chunk），按摄像机视野动态加载和卸载块
    // - 每个块内使用 Tilemap.SetTile 写入地表与阻挡图层
    // - 使用 SemaphoreSlim 限制并发加载数，使用 LRU 列表辅助管理已加载块
    public class ChunkManager : MonoBehaviour
    {
        // Tile 数据库（自定义 ScriptableObject 或其它容器）
        public TileDatabase tileDatabase;
        // 主地表 Tilemap（会被写入地形）
        public Tilemap groundTilemap; // main tilemap to write ground
        // 阻挡图层 Tilemap（用于放置阻挡瓦片，例如障碍）
        public Tilemap blockingTilemap; // tilemap for blocking tiles
        // 用于计算可见区域的摄像机，默认为 Camera.main
        public Camera mainCamera;
        // 每个 chunk 的尺寸（瓦片数量，宽高相同）
        public int chunkSize = 32;
        // 在摄像机视野外额外加载的 chunk 半径（单位：chunk）
        public int visibleRadiusInChunks = 1; // how many chunks beyond camera view to load
        // 每帧最多写入多少个瓦片，避免单帧卡顿（此处目前在 Loader 中使用）
        public int tilesPerFrame = 256;
        // 同时进行加载的最大并发数
        public int maxConcurrentLoads = 2;
        // 地图名，用于构造资源路径（如 Maps/map_0_0）
        public string mapName = "map";

        // 是否让 ChunkManager 自行合并执行（合并模式会让 ChunkManager 直接负责加载/写入/卸载）
        public bool useMergedMode = false;
        // 是否输出调试日志
        public bool enableDebugLogs = false;

        // 资源提供器（默认使用 ResourcesProvider 实现)
        private IResourceProvider _provider;
        // 引用 TilemapManager，作为唯一的写入/清理点（可通过 Inspector 赋值或自动查找）
        public TilemapManager tilemapManager;
        // 已加载块字典，键为 (chunkX, chunkY)
        private Dictionary<(int cx, int cy), LoadedChunk> _loaded = new Dictionary<(int, int), LoadedChunk>();
        // LRU 列表：用于记录最近访问的块，便于按需驱逐
        private LinkedList<(int cx, int cy)> _lru = new LinkedList<(int, int)>();
        // 为了让 TouchLru/移除为 O(1)，维护一个 key->LinkedListNode 的字典
        private Dictionary<(int cx, int cy), LinkedListNode<(int, int)>> _lruNodes = new Dictionary<(int, int), LinkedListNode<(int, int)>>();
        // 仅统计已完成加载并写入 Tilemap 的 chunk，用于驱逐策略（避免把正在加载的 chunk 误当作可驱逐对象）
        private HashSet<(int cx, int cy)> _fullyLoaded = new HashSet<(int, int)>();
        // 限制并发加载的信号量
        private SemaphoreSlim _semaphore;

        // Awake：初始化资源提供器、摄像机引用和信号量
        private void Awake()
        {
            _provider = new ResourcesProvider();
            if (mainCamera == null) mainCamera = Camera.main;
            _semaphore = new SemaphoreSlim(maxConcurrentLoads);
            // 若未在 Inspector 指定 TilemapManager，尝试自动查找场景中的实例
            if (tilemapManager == null)
            {
                tilemapManager = FindObjectOfType<TilemapManager>();
            }

            // 自动连引用：尝试在场景中找到 groundTilemap 和 tileDatabase（仅在 Inspector 未设置时）
            if (groundTilemap == null)
            {
                // 尝试从名为 Grid 的对象下找到第一个 Tilemap
                var grid = GameObject.Find("Grid");
                if (grid != null)
                {
                    var tm = grid.GetComponentInChildren<Tilemap>();
                    if (tm != null)
                    {
                        groundTilemap = tm;
                        if (enableDebugLogs) Debug.Log("ChunkManager: 在 Grid 下找到 Tilemap 并自动赋值给 groundTilemap。");
                    }
                }
                // 如果仍未找到，尝试查找场景中任何 Tilemap
                if (groundTilemap == null)
                {
                    var any = FindObjectOfType<Tilemap>();
                    if (any != null)
                    {
                        groundTilemap = any;
                        if (enableDebugLogs) Debug.Log("ChunkManager: 在场景中找到 Tilemap 并赋值给 groundTilemap。");
                    }
                }
            }

            if (tileDatabase == null)
            {
                var db = FindObjectOfType<TileDatabase>();
                if (db != null)
                {
                    tileDatabase = db;
                    if (enableDebugLogs) Debug.Log("ChunkManager: 在场景中找到 TileDatabase 并赋值。");
                }
                else
                {
                    var res = Resources.Load<TileDatabase>("TileDatabase");
                    if (res != null)
                    {
                        tileDatabase = res;
                        if (enableDebugLogs) Debug.Log("ChunkManager: 从 Resources 加载 TileDatabase 并赋值。");
                    }
                }
            }
        }

        // Update：每帧更新可见块集合并触发加载/卸载
        private void Update()
        {
            UpdateVisibleChunks();
        }

        // UpdateVisibleChunks：计算摄像机视野所需的 chunk 范围，启动缺失的加载并卸载不再需要的块
        private void UpdateVisibleChunks()
        {
            if (mainCamera == null || groundTilemap == null) return;

            // 计算摄像机在 world 空间中的矩形范围（基于摄像机与 tilemap 的 z 距离）
            float zDistance = Mathf.Abs(mainCamera.transform.position.z - groundTilemap.transform.position.z);
            Vector3 bottomLeft = mainCamera.ScreenToWorldPoint(new Vector3(0f, 0f, zDistance));
            Vector3 topRight = mainCamera.ScreenToWorldPoint(new Vector3(Screen.width, Screen.height, zDistance));

            // 将世界坐标转换为 tilemap 的 cell 坐标（整数格子坐标）
            Vector3Int blCell = groundTilemap.WorldToCell(bottomLeft);
            Vector3Int trCell = groundTilemap.WorldToCell(topRight);

            // 计算需要的 chunk 范围（chunk 的坐标为格子坐标 / chunkSize）
            int minChunkX = Mathf.FloorToInt((float)blCell.x / chunkSize) - visibleRadiusInChunks;
            int maxChunkX = Mathf.FloorToInt((float)trCell.x / chunkSize) + visibleRadiusInChunks;
            int minChunkY = Mathf.FloorToInt((float)blCell.y / chunkSize) - visibleRadiusInChunks;
            int maxChunkY = Mathf.FloorToInt((float)trCell.y / chunkSize) + visibleRadiusInChunks;

            // desired：当前帧希望存在于内存中的 chunk（使用 Vector2Int 便于与 TilemapManager 交互）
            var desired = new HashSet<Vector2Int>();
            for (int cx = minChunkX; cx <= maxChunkX; cx++)
            {
                for (int cy = minChunkY; cy <= maxChunkY; cy++)
                {
                    desired.Add(new Vector2Int(cx, cy));
                }
            }

            // 如果有 TilemapManager 并且我们不处于合并模式，则把 desired 全权委托给它
            if (tilemapManager != null && !useMergedMode)
            {
                tilemapManager.SetDesiredChunks(desired);
                if (enableDebugLogs) Debug.Log($"ChunkManager: delegated desired chunks to TilemapManager (count={desired.Count})");
                return;
            }

            // 否则继续使用当前 ChunkManager 的回退加载/卸载逻辑
            var want = new HashSet<(int, int)>();
            foreach (var d in desired) want.Add((d.x, d.y));
            for (int cx = minChunkX; cx <= maxChunkX; cx++)
            {
                for (int cy = minChunkY; cy <= maxChunkY; cy++)
                {
                    var k = (cx, cy);
                    if (!_loaded.ContainsKey(k))
                    {
                        StartLoadChunk(k.cx, k.cy).Forget();
                    }
                    else
                    {
                        TouchLru(k);
                    }
                }
            }

            // 卸载不再需要的 chunk（在 want 集合之外）
            var toRemove = new List<(int, int)>();
            foreach (var kv in _loaded)
            {
                if (!want.Contains(kv.Key)) toRemove.Add(kv.Key);
            }

            foreach (var key in toRemove)
            {
                UnloadChunk(key.Item1, key.Item2);
            }
        }

        // TouchLru：把访问的 key 移到 LRU 列表头部（表示最近使用）
        private void TouchLru((int cx, int cy) key)
        {
            // O(1) 触碰：如果已有节点则移到头部，否则新增头部并记入字典
            if (_lruNodes.TryGetValue(key, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                _lruNodes[key] = _lru.First; // 更新引用（First 返回新节点）
            }
            else
            {
                var newNode = _lru.AddFirst(key);
                _lruNodes[key] = newNode;
            }
        }

        // StartLoadChunk：开始异步加载一个 chunk。使用 UniTaskVoid 因为这是 fire-and-forget 风格的启动器。
        // 流程：获取信号量 -> 注册 LoadedChunk -> 调用 TilemapLoader.LoadChunkAsync -> 释放信号量
        private async UniTaskVoid StartLoadChunk(int cx, int cy)
        {
            await _semaphore.WaitAsync();
            var key = (cx, cy);
            var resourcePath = $"Maps/{mapName}_{cx}_{cy}"; // 资源路径命名约定
            var cts = new CancellationTokenSource();
            // 这里只记录 CancellationTokenSource（其它信息通过外部字典和 LRU 管理）
            var lc = new LoadedChunk { Cts = cts, Unloading = false };
            _loaded[key] = lc;
            TouchLru(key);

            try
            {
                // 使用新的 Loader API 只加载并返回数据
                var data = await TilemapLoader.LoadChunkDataAsync(_provider, resourcePath, cts.Token);
                if (data == null)
                {
                    // 未找到或解析失败
                    return;
                }

                // 确保在主线程写入
                await UniTask.SwitchToMainThread(cts.Token);

                int w = data.width;
                int h = data.height;
                int count = 0;
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        cts.Token.ThrowIfCancellationRequested();
                        int idx = y * w + x;
                        int id = 0;
                        if (data.tiles != null && idx < data.tiles.Length) id = data.tiles[idx];

                        TileBase tile = tileDatabase.GetTileById(id);
                        Vector3Int cell = new Vector3Int(data.originX + x, data.originY + y, 0);
                        groundTilemap.SetTile(cell, tile);

                        if (blockingTilemap != null && data.blocking != null && idx < data.blocking.Length)
                        {
                            byte b = data.blocking[idx];
                            if (b != 0) blockingTilemap.SetTile(cell, tile);
                            else blockingTilemap.SetTile(cell, null);
                        }

                        count++;
                        if (count % tilesPerFrame == 0)
                        {
                            await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate, cts.Token);
                        }
                    }
                }

                // 加载成功并写入 Tilemap 后，标记为已完成加载并触碰 LRU
                _fullyLoaded.Add(key);
                TouchLru(key);
                // 如果已加载数量超限，驱逐最久未使用的已加载块
                EvictIfNeeded();
            }
            catch (OperationCanceledException)
            {
                // 被取消（例如卸载时），直接忽略
            }
            catch (Exception ex)
            {
                // 记录加载出错信息，但不抛出以免影响主循环
                Debug.LogWarning($"LoadChunkDataAsync failed {cx},{cy}: {ex}");
            }
            finally
            {
                _semaphore.Release();
            }
        }

        // UnloadChunk 同步入口（保持兼容调用点），内部调用异步实现并 fire-and-forget
        private void UnloadChunk(int cx, int cy)
        {
            UnloadChunkAsync(cx, cy).Forget();
        }

        // UnloadChunkAsync：真正的异步卸载流程，顺序为：取消加载 -> 等待/执行清理 -> 移除内部状态
        private async UniTaskVoid UnloadChunkAsync(int cx, int cy)
        {
            var key = (cx, cy);
            if (!_loaded.TryGetValue(key, out var lc)) return;
            // 如果已在卸载中，直接返回，避免重复并发卸载
            if (lc.Unloading) return;
            lc.Unloading = true;

            // 先取消正在进行的加载任务（如果有）并释放 CTS
            try { lc.Cts.Cancel(); lc.Cts.Dispose(); } catch (Exception ex) { Debug.LogWarning($"UnloadChunk Cancel failed {cx},{cy}: {ex}"); }

            // 执行清理：在主线程执行 Tile 清理（不再依赖 tilemapManager 的方法以避免符号解析问题）
            try
            {
                await UniTask.SwitchToMainThread();
                int ox = cx * chunkSize;
                int oy = cy * chunkSize;
                for (int y = 0; y < chunkSize; y++)
                {
                    for (int x = 0; x < chunkSize; x++)
                    {
                        Vector3Int cell = new Vector3Int(ox + x, oy + y, 0);
                        groundTilemap.SetTile(cell, null);
                        if (blockingTilemap != null) blockingTilemap.SetTile(cell, null);
                    }
                }
                if (tilemapManager != null && enableDebugLogs) Debug.Log($"ChunkManager: cleared chunk {cx},{cy} via local clear (tilemapManager present)");
                if (tilemapManager == null && enableDebugLogs) Debug.Log($"ChunkManager: cleared chunk {cx},{cy} via local clear (no tilemapManager)");
            }
            catch (OperationCanceledException)
            {
                // 如果清理被取消，继续完成后续状态移除
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"UnloadChunkAsync clear failed {cx},{cy}: {ex}");
            }

            // 最后从已加载字典和 LRU 中移除记录
            _loaded.Remove(key);
            _fullyLoaded.Remove(key);
            if (_lruNodes.TryGetValue(key, out var node))
            {
                _lru.Remove(node);
                _lruNodes.Remove(key);
            }
        }

        // LoadedChunk：保存已加载 chunk 的简单状态
        private class LoadedChunk
        {
            // 仅保留需要的字段：用于取消加载任务的 CancellationTokenSource
            public CancellationTokenSource Cts;
            // 标记是否已进入卸载流程，避免重复卸载
            public bool Unloading;
        }

        // 最大同时保留在内存中的已加载块数（不会影响正在加载但未完成的块）
        public int maxLoadedChunks = 8;

        // 驱逐检查：当已完成加载的 chunk 数超过阈值时，从 LRU 的尾部驱逐
        private void EvictIfNeeded()
        {
            // 注意：所有调用都在主线程（Update/StartLoadChunk/UnloadChunk），因此可以安全操作集合
            while (_fullyLoaded.Count > maxLoadedChunks)
            {
                // 找到 LRU 尾部
                var lastNode = _lru.Last;
                if (lastNode == null) break;
                var key = lastNode.Value;

                // 如果尾部不是已完成加载的 chunk（可能是正在加载的占位），只移除它的 LRU 记录
                if (!_fullyLoaded.Contains(key))
                {
                    _lru.Remove(lastNode);
                    _lruNodes.Remove(key);
                    continue;
                }

                // 卸载此 chunk（UnloadChunk 会同时移除 _fullyLoaded 与 _lruNodes）
                UnloadChunk(key.cx, key.cy);
            }
        }
    }
}
