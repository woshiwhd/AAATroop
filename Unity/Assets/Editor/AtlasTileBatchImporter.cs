using System.Collections.Generic;
using Script.Utilities;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Editor
{
    /// <summary>
    /// 图集子 Sprite 排序：先按 rect.yMin 降序（纹理上方先），再按 xMin 升序（同行从左到右）。
    /// 生成顺序与 TileDatabase 追加顺序一致：索引 i 对应 row=i/cols、col=i%cols，与程序化
    /// atlasIndex = mod(worldY,rows)*cols + mod(worldX,cols) 连续对齐（若世界 Y 与纹理上下相反需对 row 取反）。
    /// </summary>
    public class AtlasTileBatchImporter : EditorWindow
    {
        private Texture2D _atlas;
        private DefaultAsset _outputFolder;
        private TileDatabase _tileDatabase;
        /// <summary>为 true 时把新 Tile 接到列表末尾；为 false 时从 <see cref="_databaseStartIndex"/> 起覆盖写入，不拉长多余项。</summary>
        private bool _appendToEndOfDatabase;
        private int _databaseStartIndex;
        private string _namePrefix = "grass_atlas";

        [MenuItem("Tools/Map/Batch Tiles From Atlas (Sorted)")]
        public static void Open()
        {
            GetWindow<AtlasTileBatchImporter>("Atlas Tile Batch").minSize = new Vector2(420, 280);
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "排序：自上而下（yMin 降序）、从左到右（xMin 升序）。\n" +
                "列表第 0 项 = 图集最上行最左格；i → row=i/cols, col=i%cols。\n" +
                "程序化：atlasIndex = mod(wy,rows)*cols + mod(wx,cols)；id = baseId + atlasIndex。\n" +
                "同一输出路径下会覆盖已有 .asset（不另存 _1/_2）；默认从数据库下标 0 起覆盖，不勾选「追加」则列表长度不会每次变长。",
                MessageType.Info);
            _atlas = (Texture2D)EditorGUILayout.ObjectField("图集 (Multiple)", _atlas, typeof(Texture2D), false);
            _outputFolder = (DefaultAsset)EditorGUILayout.ObjectField("输出文件夹", _outputFolder, typeof(DefaultAsset), false);
            _namePrefix = EditorGUILayout.TextField("资源名前缀", _namePrefix);
            _tileDatabase = (TileDatabase)EditorGUILayout.ObjectField("TileDatabase", _tileDatabase, typeof(TileDatabase), false);
            _appendToEndOfDatabase = EditorGUILayout.ToggleLeft("追加到 TileDatabase 末尾（否则覆盖指定下标起的连续项）", _appendToEndOfDatabase);
            using (new EditorGUI.DisabledScope(_appendToEndOfDatabase))
            {
                _databaseStartIndex = EditorGUILayout.IntField("覆盖起始下标 (0-based)", _databaseStartIndex);
            }
            if (GUILayout.Button("生成")) Run();
        }

        private void Run()
        {
            if (_atlas == null)
            {
                EditorUtility.DisplayDialog("Atlas Tile Batch", "请指定图集纹理。", "OK");
                return;
            }

            string atlasPath = AssetDatabase.GetAssetPath(_atlas);
            var sprites = new List<Sprite>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(atlasPath))
                if (o is Sprite sp) sprites.Add(sp);

            if (sprites.Count == 0)
            {
                EditorUtility.DisplayDialog("Atlas Tile Batch", "未找到子 Sprite，请使用 Multiple 并 Slice。", "OK");
                return;
            }

            sprites.Sort((a, b) =>
            {
                int yc = b.rect.yMin.CompareTo(a.rect.yMin);
                return yc != 0 ? yc : a.rect.xMin.CompareTo(b.rect.xMin);
            });

            int cellW = Mathf.RoundToInt(sprites[0].rect.width);
            int cellH = Mathf.RoundToInt(sprites[0].rect.height);
            int cols = Mathf.Max(1, _atlas.width / cellW);
            int rows = Mathf.Max(1, _atlas.height / cellH);
            int expected = cols * rows;

            if (sprites.Count != expected &&
                !EditorUtility.DisplayDialog("Atlas Tile Batch",
                    $"子图数量 {sprites.Count} 与网格 {cols}x{rows}={expected} 不一致，仍继续？", "继续", "取消"))
                return;

            string outDir = _outputFolder == null ? "Assets/Tiles/AtlasGenerated" : AssetDatabase.GetAssetPath(_outputFolder);
            if (string.IsNullOrEmpty(outDir)) outDir = "Assets/Tiles/AtlasGenerated";

            if (!AssetDatabase.IsValidFolder(outDir))
            {
                string parent = "Assets";
                foreach (var part in outDir.Replace("\\", "/").Split('/'))
                {
                    if (part == "Assets" || string.IsNullOrEmpty(part)) continue;
                    string next = parent + "/" + part;
                    if (!AssetDatabase.IsValidFolder(next))
                        AssetDatabase.CreateFolder(parent, part);
                    parent = next;
                }
            }

            var created = new List<TileBase>();
            try
            {
                AssetDatabase.StartAssetEditing();
                for (int i = 0; i < sprites.Count; i++)
                {
                    int row = i / cols;
                    int col = i % cols;
                    string tileBaseName = $"{_namePrefix}_r{row:D2}c{col:D2}";
                    string path = $"{outDir}/{tileBaseName}.asset".Replace("\\", "/");
                    var existing = AssetDatabase.LoadAssetAtPath<Tile>(path);
                    if (existing != null)
                    {
                        existing.name = tileBaseName;
                        existing.sprite = sprites[i];
                        EditorUtility.SetDirty(existing);
                        created.Add(existing);
                        continue;
                    }

                    if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
                        AssetDatabase.DeleteAsset(path);

                    var tile = ScriptableObject.CreateInstance<Tile>();
                    tile.name = tileBaseName;
                    tile.sprite = sprites[i];
                    AssetDatabase.CreateAsset(tile, path);
                    created.Add(tile);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            if (_tileDatabase != null && created.Count > 0)
            {
                var so = new SerializedObject(_tileDatabase);
                var prop = so.FindProperty("tiles");
                if (prop != null && prop.isArray)
                {
                    if (_appendToEndOfDatabase)
                    {
                        int start = prop.arraySize;
                        prop.arraySize = start + created.Count;
                        for (int i = 0; i < created.Count; i++)
                            prop.GetArrayElementAtIndex(start + i).objectReferenceValue = created[i];
                    }
                    else
                    {
                        int start = Mathf.Max(0, _databaseStartIndex);
                        int need = start + created.Count;
                        if (prop.arraySize < need)
                            prop.arraySize = need;
                        for (int i = 0; i < created.Count; i++)
                            prop.GetArrayElementAtIndex(start + i).objectReferenceValue = created[i];
                    }

                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(_tileDatabase);
                    AssetDatabase.SaveAssets();
                }
            }

            Debug.Log($"AtlasTileBatchImporter: 生成 {created.Count} 个 Tile，cols={cols}，rows≈{(created.Count + cols - 1) / cols}。");
            EditorUtility.DisplayDialog("Atlas Tile Batch", $"已生成 {created.Count} 个 Tile。\n{outDir}", "OK");
        }
    }
}
