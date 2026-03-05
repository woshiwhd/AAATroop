using UnityEngine;
using UnityEngine.Tilemaps;
using Script.Managers;

namespace Script.Debug
{
    /// <summary>
    /// 编辑器中绘制 Chunk 调试信息（已加载 chunk 边界、相机视野）。
    /// 可挂在任意 GameObject（如 ChunkManager 所在物体）；仅 Scene 视图可见。
    /// </summary>
    [ExecuteAlways]
    public class ChunkDebugDraw : MonoBehaviour
    {
        [Header("显示选项")]
        [Tooltip("是否绘制已加载 chunk 的边界")]
        public bool drawLoadedChunks = true;
        [Tooltip("是否绘制相机视野范围")]
        public bool drawCameraBounds = true;

        private ChunkManager _chunkMgr;
        private TilemapManager _tilemapMgr;

        private void OnDrawGizmos()
        {
            if (!DebugDisplayManager.EnableAllDebugDraws) return;
            if (!drawLoadedChunks && !drawCameraBounds) return;

            _chunkMgr = _chunkMgr != null ? _chunkMgr : FindObjectOfType<ChunkManager>();
            _tilemapMgr = _tilemapMgr != null ? _tilemapMgr : FindObjectOfType<TilemapManager>();
            if (_chunkMgr == null && _tilemapMgr == null) return;

            Tilemap tilemap = null;
            int cw = 32, ch = 32;
            Camera cam = null;

            if (_chunkMgr != null)
            {
                tilemap = _chunkMgr.DebugGetTilemap();
                cw = ch = _chunkMgr.DebugGetChunkSize();
                cam = _chunkMgr.DebugGetCamera();
                if (drawLoadedChunks && tilemap != null)
                {
                    Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.5f);
                    foreach (var (cx, cy) in _chunkMgr.DebugGetLoadedChunkCoords())
                        DrawChunkBounds(tilemap, cx, cy, cw, ch);
                }
            }
            else if (_tilemapMgr != null)
            {
                tilemap = _tilemapMgr.DebugGetTilemap();
                cw = _tilemapMgr.DebugGetChunkWidth();
                ch = _tilemapMgr.DebugGetChunkHeight();
                cam = _tilemapMgr.DebugGetCamera();
                if (drawLoadedChunks && tilemap != null)
                {
                    Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.5f);
                    foreach (var v in _tilemapMgr.DebugGetLoadedChunkCoords())
                        DrawChunkBounds(tilemap, v.x, v.y, cw, ch);
                }
            }

            if (tilemap == null && !drawCameraBounds) return;

            if (drawCameraBounds && cam != null)
            {
                Gizmos.color = new Color(1f, 1f, 0f, 0.6f);
                float z = (tilemap != null ? tilemap.transform.position.z : cam.transform.position.z - 10f);
                float dist = Mathf.Abs(cam.transform.position.z - z);
                var bl = cam.ScreenToWorldPoint(new Vector3(0, 0, dist));
                var tr = cam.ScreenToWorldPoint(new Vector3(Screen.width, Screen.height, dist));
                DrawRectXy(bl, tr, z);
            }
        }

        private void DrawChunkBounds(Tilemap tm, int cx, int cy, int cw, int ch)
        {
            var cellMin = new Vector3Int(cx * cw, cy * ch, 0);
            var cellMax = new Vector3Int((cx + 1) * cw - 1, (cy + 1) * ch - 1, 0);
            var w0 = tm.CellToWorld(cellMin);
            var w1 = tm.CellToWorld(cellMax);
            var w2 = tm.CellToWorld(new Vector3Int(cellMax.x, cellMin.y, 0));
            var w3 = tm.CellToWorld(new Vector3Int(cellMin.x, cellMax.y, 0));
            float z = w0.z;
            Gizmos.DrawLine(w0, w2);
            Gizmos.DrawLine(w2, w1);
            Gizmos.DrawLine(w1, w3);
            Gizmos.DrawLine(w3, w0);
        }

        private static void DrawRectXy(Vector3 bl, Vector3 tr, float z)
        {
            bl.z = z; tr.z = z;
            var br = new Vector3(tr.x, bl.y, z);
            var tl = new Vector3(bl.x, tr.y, z);
            Gizmos.DrawLine(bl, br);
            Gizmos.DrawLine(br, tr);
            Gizmos.DrawLine(tr, tl);
            Gizmos.DrawLine(tl, bl);
        }
    }
}
