using UnityEngine;
using UnityEditor;

// Editor window 用于配置 Tilemap 调试绘制设置（仅编辑器使用）
public class TilemapDebugWindow : EditorWindow
{
    // 编辑器配置在静态类中保存，Drawer 会读取它们
    public static class Settings
    {
        public static bool showIDs = true;
        public static Script.TilemapManager targetManager = null;
        public static float updateFrequency = 0.2f;
        // 新增设置
        public static bool showTemplateName = true;
        public static bool showTileIDs = false;
        public static float tileIdMinPixel = 12f; // 当一个 tile 在屏幕上小于此像素则不绘制 tile id
    }

    [MenuItem("Window/Tilemap Debug Window")]
    public static void ShowWindow()
    {
        var w = GetWindow<TilemapDebugWindow>("Tilemap Debug");
        w.minSize = new Vector2(240, 160);
    }

    private void OnEnable()
    {
        // nothing for now
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Tilemap Debugger", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();

        Settings.showIDs = EditorGUILayout.Toggle("Show Chunk IDs", Settings.showIDs);
        Settings.targetManager = (Script.TilemapManager)EditorGUILayout.ObjectField("Target TilemapManager", Settings.targetManager, typeof(Script.TilemapManager), true);
        Settings.updateFrequency = EditorGUILayout.FloatField("Update Frequency (s)", Settings.updateFrequency);
        if (Settings.updateFrequency < 0.05f) Settings.updateFrequency = 0.05f;

        GUILayout.Space(6);
        Settings.showTemplateName = EditorGUILayout.Toggle("Show Template Name", Settings.showTemplateName);
        Settings.showTileIDs = EditorGUILayout.Toggle("Show Tile IDs", Settings.showTileIDs);
        EditorGUI.BeginDisabledGroup(!Settings.showTileIDs);
        Settings.tileIdMinPixel = EditorGUILayout.FloatField("Tile ID Min Pixel", Settings.tileIdMinPixel);
        if (Settings.tileIdMinPixel < 4f) Settings.tileIdMinPixel = 4f;
        EditorGUI.EndDisabledGroup();

        if (EditorGUI.EndChangeCheck())
        {
            // 通知 Drawer 强制刷新
            TilemapDebugDrawer.ForceRefresh();
            SceneView.RepaintAll();
        }

        GUILayout.Space(8);
        if (GUILayout.Button("Force Refresh"))
        {
            TilemapDebugDrawer.ForceRefresh();
            SceneView.RepaintAll();
        }

        EditorGUILayout.HelpBox("仅在编辑器 Scene 视图中绘制 chunk id（安全、不会影响运行时发布）。\n先只显示 chunk id，后续可按需扩展显示 tile 属性。", MessageType.Info);
    }
}
