using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Script.Utilities;

namespace Script.Managers
{
    /// <summary>
    /// ChunkManager：地图系统的“视野调度层”。
    /// - 仅负责：按相机视野计算 desired chunks
    /// - 加载/卸载与 Tilemap 写入：统一委托给 TilemapManager（唯一写入点）
    /// </summary>
    public class ChunkManager : MonoBehaviour
    {
        [Header("References")]
        public Camera mainCamera;

        [Tooltip("用于世界坐标 <-> cell 坐标转换的 Tilemap（通常为地表层）")]
        public Tilemap groundTilemap;

        [Tooltip("TilemapManager：执行加载/卸载并写入 Tilemap")]
        public TilemapManager tilemapManager;

        [Header("Chunk Settings")]
        public int chunkSize = 32;
        public int visibleRadiusInChunks = 1;

        [Header("Debug")]
        public bool enableDebugLogs = false;

        private void Awake()
        {
            if (mainCamera == null) mainCamera = Camera.main;

            if (tilemapManager == null)
                tilemapManager = FindObjectOfType<TilemapManager>();

            if (groundTilemap == null)
            {
                var grid = GameObject.Find("Grid");
                if (grid != null)
                {
                    var tm = grid.GetComponentInChildren<Tilemap>();
                    if (tm != null)
                    {
                        groundTilemap = tm;
                        if (enableDebugLogs) GameLog.Log("ChunkManager: 在 Grid 下找到 Tilemap 并自动赋值给 groundTilemap。");
                    }
                }

                if (groundTilemap == null)
                {
                    var any = FindObjectOfType<Tilemap>();
                    if (any != null)
                    {
                        groundTilemap = any;
                        if (enableDebugLogs) GameLog.Log("ChunkManager: 在场景中找到 Tilemap 并赋值给 groundTilemap。");
                    }
                }
            }
        }

        private void Update()
        {
            UpdateVisibleChunks();
        }

        private void UpdateVisibleChunks()
        {
            if (mainCamera == null || groundTilemap == null) return;
            if (tilemapManager == null)
            {
                GameLog.LogError("ChunkManager: tilemapManager 未设置，无法委托加载/卸载。");
                enabled = false;
                return;
            }

            // 核心算法：相机视野 -> Tilemap cell 矩形 -> chunk 坐标范围 -> desired 集合
            //
            // 1) 取屏幕左下/右上投影到 Tilemap 所在平面（按相机 z 与 Tilemap z 的距离）
            // 2) 用 Tilemap.WorldToCell 转成 cell 坐标（整数格子）
            // 3) cell / chunkSize => chunk 坐标，并扩一圈 padding（visibleRadiusInChunks）
            // 4) 遍历 chunk 坐标矩形，构造 desired（HashSet 去重）
            //
            // 复杂度：O(NchunksInView)，N 与视野覆盖的 chunk 数量成正比；通常远小于逐 tile 扫描。
            float zDistance = Mathf.Abs(mainCamera.transform.position.z - groundTilemap.transform.position.z);
            Vector3 bottomLeft = mainCamera.ScreenToWorldPoint(new Vector3(0f, 0f, zDistance));
            Vector3 topRight = mainCamera.ScreenToWorldPoint(new Vector3(Screen.width, Screen.height, zDistance));

            Vector3Int blCell = groundTilemap.WorldToCell(bottomLeft);
            Vector3Int trCell = groundTilemap.WorldToCell(topRight);

            int minChunkX = Mathf.FloorToInt((float)blCell.x / chunkSize) - visibleRadiusInChunks;
            int maxChunkX = Mathf.FloorToInt((float)trCell.x / chunkSize) + visibleRadiusInChunks;
            int minChunkY = Mathf.FloorToInt((float)blCell.y / chunkSize) - visibleRadiusInChunks;
            int maxChunkY = Mathf.FloorToInt((float)trCell.y / chunkSize) + visibleRadiusInChunks;

            var desired = new HashSet<Vector2Int>();
            for (int cx = minChunkX; cx <= maxChunkX; cx++)
            {
                for (int cy = minChunkY; cy <= maxChunkY; cy++)
                    desired.Add(new Vector2Int(cx, cy));
            }

            // 委托给 TilemapManager：它负责增量加载/卸载并写入 Tilemap。
            tilemapManager.SetDesiredChunks(desired);
            if (enableDebugLogs) GameLog.Log($"ChunkManager: delegated desired chunks to TilemapManager (count={desired.Count})");
        }

#if UNITY_EDITOR
        public IEnumerable<(int cx, int cy)> DebugGetLoadedChunkCoords()
        {
            if (tilemapManager == null) yield break;
            foreach (var v in tilemapManager.DebugGetLoadedChunkCoords())
                yield return (v.x, v.y);
        }

        public Tilemap DebugGetTilemap() => groundTilemap;
        public int DebugGetChunkSize() => chunkSize;
        public Camera DebugGetCamera() => mainCamera;
#endif
    }
}
