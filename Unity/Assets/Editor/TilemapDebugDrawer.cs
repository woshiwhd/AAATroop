using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.Tilemaps;
using Script; // 引入 TileDatabase 等在 Script 命名空间下的类型

#if UNITY_EDITOR
[InitializeOnLoad]
public static class TilemapDebugDrawer
{
    static TilemapDebugDrawer()
    {
        // 注册 SceneView 回调
        SceneView.duringSceneGui += OnSceneGUI;
        lastUpdateTime = EditorApplication.timeSinceStartup;
    }

    private static double lastUpdateTime = 0;
    private static List<Vector2Int> cachedVisibleChunks = new List<Vector2Int>();
    private static bool needsRefresh = true;

    public static void ForceRefresh()
    {
        needsRefresh = true;
        lastUpdateTime = 0;
    }

    private static void OnSceneGUI(SceneView sv)
    {
        if (!TilemapDebugWindow.Settings.showIDs) return;
        var mgr = TilemapDebugWindow.Settings.targetManager;
        if (mgr == null) return;

        double now = EditorApplication.timeSinceStartup;
        if (!needsRefresh && now - lastUpdateTime < TilemapDebugWindow.Settings.updateFrequency)
        {
            // 直接用 cache 渲染
            DrawCached(sv);
            return;
        }

        // 更新缓存
        lastUpdateTime = now;
        needsRefresh = false;
        cachedVisibleChunks.Clear();

        try
        {
            // 尝试通过 SerializedObject 读取序列化字段
            SerializedObject so = new SerializedObject(mgr);
            var targetTilemapProp = so.FindProperty("targetTilemap");
            var chunkWidthProp = so.FindProperty("chunkWidth");
            var chunkHeightProp = so.FindProperty("chunkHeight");

            Tilemap tilemap = null;
            int chunkW = 32;
            int chunkH = 32;

            if (targetTilemapProp != null)
            {
                var obj = targetTilemapProp.objectReferenceValue;
                tilemap = obj as Tilemap;
            }
            if (chunkWidthProp != null) chunkW = chunkWidthProp.intValue;
            if (chunkHeightProp != null) chunkH = chunkHeightProp.intValue;

            if (tilemap == null)
            {
                // 尝试通过反射访问私有字段 targetTilemap
                var t = mgr.GetType();
                var f = t.GetField("targetTilemap", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (f != null) tilemap = f.GetValue(mgr) as Tilemap;
            }

            if (tilemap == null) return;

            // 计算 SceneView 相机视野的 world 四角
            Camera cam = sv.camera;
            float zDistance = Mathf.Abs(cam.transform.position.z - tilemap.transform.position.z);
            Vector3 bl = cam.ScreenToWorldPoint(new Vector3(0, 0, zDistance));
            Vector3 tr = cam.ScreenToWorldPoint(new Vector3(sv.position.width, sv.position.height, zDistance));

            Vector3Int cellBL = tilemap.WorldToCell(bl);
            Vector3Int cellTR = tilemap.WorldToCell(tr);

            int minChunkX = Mathf.FloorToInt((float)cellBL.x / chunkW);
            int maxChunkX = Mathf.FloorToInt((float)cellTR.x / chunkW);
            int minChunkY = Mathf.FloorToInt((float)cellBL.y / chunkH);
            int maxChunkY = Mathf.FloorToInt((float)cellTR.y / chunkH);

            for (int cx = minChunkX; cx <= maxChunkX; cx++)
            {
                for (int cy = minChunkY; cy <= maxChunkY; cy++)
                {
                    cachedVisibleChunks.Add(new Vector2Int(cx, cy));
                }
            }
        }
        catch (Exception ex)
        {
            // 保护性捕获，避免编辑器窗口崩溃
            Debug.LogWarning($"TilemapDebugDrawer: exception while computing visible chunks: {ex}");
            return;
        }

        DrawCached(sv);
    }

    private static void DrawCached(SceneView sv)
    {
        Handles.BeginGUI();
        try
        {
            foreach (var c in cachedVisibleChunks)
            {
                // 计算 chunk 世界中心并绘制标签
                var mgr = TilemapDebugWindow.Settings.targetManager;
                if (mgr == null) break;

                // 获取 chunk size
                int chunkW = 32; int chunkH = 32;
                try
                {
                    SerializedObject so = new SerializedObject(mgr);
                    var cw = so.FindProperty("chunkWidth"); if (cw != null) chunkW = cw.intValue;
                    var ch = so.FindProperty("chunkHeight"); if (ch != null) chunkH = ch.intValue;
                }
                catch { }

                float centerX = (c.x * chunkW) + chunkW * 0.5f;
                float centerY = (c.y * chunkH) + chunkH * 0.5f;
                Vector3 worldPos = new Vector3(centerX, centerY, 0);

                Vector3 guiPos = HandleUtility.WorldToGUIPoint(worldPos);
                var rect = new Rect(guiPos.x - 40, guiPos.y - 10, 80, 20);
                GUIStyle style = new GUIStyle(EditorStyles.label);
                style.normal.textColor = Color.yellow;
                GUI.Label(rect, $"Chunk {c.x},{c.y}", style);

                // 如果开启模板名显示，则尝试读取已加载的 chunkData 并显示 templateName 或标记为未加载
                if (TilemapDebugWindow.Settings.showTemplateName)
                {
                    string tplName = null;
                    try
                    {
                        if (mgr.TryGetChunkData(c, out var cd) && cd != null && !string.IsNullOrEmpty(cd.templateName)) tplName = cd.templateName;
                    }
                    catch { }

                    string line = tplName != null ? tplName : "(unloaded)";
                    var rect2 = new Rect(guiPos.x - 40, guiPos.y + 8, 120, 18);
                    GUIStyle style2 = new GUIStyle(EditorStyles.miniLabel);
                    style2.normal.textColor = tplName != null ? Color.cyan : Color.gray;
                    GUI.Label(rect2, line, style2);
                }

                // 如果开启显示 tile id 并且缩放允许，则在 chunk 内绘制部分 tile id（受数量限制）
                if (TilemapDebugWindow.Settings.showTileIDs && TilemapDebugWindow.Settings.tileIdMinPixel > 0f)
                {
                    // 估算单个 tile 在屏幕上的像素大小：取 world size 为 1 unit -> 转换为 GUI 点
                    Vector3 worldTileRight = HandleUtility.WorldToGUIPoint(new Vector3(centerX + 1f, centerY, 0));
                    float pixelPerUnit = Mathf.Abs(worldTileRight.x - guiPos.x);
                    if (pixelPerUnit >= TilemapDebugWindow.Settings.tileIdMinPixel)
                    {
                        // 获取 chunk 数据（如果加载）并绘制其 tiles 的 id（限制每chunk绘制上限以避免卡顿）
                        try
                        {
                            if (mgr.TryGetChunkData(c, out var cd) && cd != null && cd.tiles != null)
                            {
                                int w = cd.width;
                                int h = cd.height;
                                int drawLimit = 4000; // 每 chunk 最多绘制多少 tile id
                                int drawn = 0;

                                // 从 mgr 的序列化属性安全读取 tileDatabase 引用（如果有）
                                TileDatabase db = null;
                                try {
                                    SerializedObject so = new SerializedObject(mgr);
                                    var td = so.FindProperty("tileDatabase");
                                    if (td != null) db = td.objectReferenceValue as TileDatabase;
                                } catch {}

                                for (int y = 0; y < h; y++)
                                {
                                    for (int x = 0; x < w; x++)
                                    {
                                        if (drawn >= drawLimit) break;
                                        int idx = y * w + x;
                                        int id = 0;
                                        if (cd.tiles != null && idx < cd.tiles.Length) id = cd.tiles[idx];

                                        // 构建显示字符串：优先显示 raw id；如果有 TileDatabase，则附带 tile 名称，便于调试。
                                        string disp = id.ToString();
                                        if (db != null)
                                        {
                                            try
                                            {
                                                var tile = db.GetTileById(id);
                                                if (tile != null) disp = $"{id}:{tile.name}";
                                                else disp = $"{id}:(null)";
                                            }
                                            catch { /* 容错，避免编辑器绘制抛异常 */ }
                                        }

                                        Vector3 tileWorld = new Vector3(cd.originX + x + 0.5f, cd.originY + y + 0.5f, 0);
                                        Vector3 tileGui = HandleUtility.WorldToGUIPoint(tileWorld);
                                        var r = new Rect(tileGui.x - 12, tileGui.y - 8, 40, 16);
                                        GUIStyle s = new GUIStyle(EditorStyles.miniLabel);
                                        s.normal.textColor = Color.white;
                                        GUI.Label(r, disp, s);
                                        drawn++;
                                    }
                                    if (drawn >= drawLimit) break;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            // 容错：避免在 Scene 绘制时抛异常
                            Debug.LogWarning($"TilemapDebugDrawer: draw tile ids failed for chunk {c}: {ex}");
                        }
                    }
                }
            }
        }
        finally
        {
            Handles.EndGUI();
        }
    }
}
#endif

