using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Tilemaps;
using Script.Utilities;

namespace Script.Managers
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
        private Script.Core.IResourceProvider _provider;
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
        // 卸载时清空瓦片用（SetTilesBlock 需传 null 数组才能正确清空）
        private TileBase[] _clearTilesBuffer;

        // Awake：初始化资源提供器、摄像机引用和信号量
        private void Awake()
        {
            _provider = new Script.Core.ResourcesProvider();
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
                // 计算 chunk 在 tilemap 中的起始位置（单位：瓦片）
                int startX = cx * chunkSize;
                int startY = cy * chunkSize;

                // 写入地表层和阻挡层（假设数据格式为一维数组，按行优先存储）
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        // 计算全局坐标
                        int globalX = startX + x;
                        int globalY = startY + y;

                        // 写入地表层
                        if (data.ground != null && data.ground[y * w + x] != 0)
                        {
                            var tile = tileDatabase.GetTileById(data.ground[y * w + x]);
                            groundTilemap.SetTile(new Vector3Int(globalX, globalY, 0), tile);
                        }

                        // 写入阻挡层
                        if (data.blocking != null && data.blocking[y * w + x] != 0)
                        {
                            var tile = tileDatabase.GetTileById(data.blocking[y * w + x]);
                            blockingTilemap.SetTile(new Vector3Int(globalX, globalY, 0), tile);
                        }
                    }
                }

                // 标记为完全加载（用于驱逐策略）
                _fullyLoaded.Add(key);
            }
            catch (Exception e)
            {
                Debug.LogError($"ChunkManager: 加载 chunk ({cx}, {cy}) 失败: {e}");
            }
            finally
            {
                // 释放信号量
                _semaphore.Release();
            }
        }

        // UnloadChunk：卸载指定的 chunk
        // - 移除 Tilemap 上的所有瓦片
        // - 取消加载中的请求并标记为卸载中
        // - 从已加载字典和 LRU 中移除
        private void UnloadChunk(int cx, int cy)
        {
            var key = (cx, cy);
            if (_loaded.TryGetValue(key, out var lc))
            {
                // 标记为卸载中
                lc.Unloading = true;

                // 取消加载请求
                lc.Cts.Cancel();

                // 等待一帧后清除瓦片并移除记录（.Forget() 确保延后逻辑会被执行）
                int size = chunkSize * chunkSize;
                if (_clearTilesBuffer == null || _clearTilesBuffer.Length < size)
                    _clearTilesBuffer = new TileBase[size];
                var bounds = new BoundsInt(cx * chunkSize, cy * chunkSize, 0, chunkSize, chunkSize, 1);
                UniTask.DelayFrame(1).ContinueWith(() =>
                {
                    foreach (var layer in new[] { groundTilemap, blockingTilemap })
                    {
                        if (layer != null)
                            layer.SetTilesBlock(bounds, _clearTilesBuffer);
                    }
                    _loaded.Remove(key);
                    if (_lruNodes.TryGetValue(key, out var node))
                    {
                        _lru.Remove(node);
                        _lruNodes.Remove(key);
                    }
                    _fullyLoaded.Remove(key);
                }).Forget();
            }
        }
    }

    // LoadedChunk：表示一个已加载的块（chunk），包含加载状态和取消令牌源
    [System.Diagnostics.DebuggerDisplay("LoadedChunk ({cx}, {cy})")]
    public class LoadedChunk
    {
        // 取消令牌源，用于取消正在进行的加载操作
        public CancellationTokenSource Cts;
        // 是否正在卸载中
        public bool Unloading;
    }
}
