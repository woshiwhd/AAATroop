using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Script;
using Script.Managers;
using Script.Utilities;
using UnityEditor;
using UnityEditor.U2D.Animation;
using UnityEngine;
using UnityEngine.Tilemaps;

// 编辑器工具：块模板编辑器
// 功能（精简）：
// - 从 Resources/chunk_templates 加载 JSON 模板
// - 编辑每格的 tile id 和 blocking 字节
// - 从场景的 TilemapManager 强制块大小或手动输入
// - Save As -> 写入 Assets/Resources/chunk_templates/{name}.json

namespace Editor
{
    public class ChunkTemplateEditorWindow : EditorWindow
    {
        private const string ResourcesPath = "chunk_templates";

        private Vector2 _leftScroll;
        private Vector2 _rightScroll;

        // 从 Resources 加载的模板
        private List<TextAsset> _templateAssets = new List<TextAsset>();
        private List<TilemapLoader.ChunkData> _templateDatas = new List<TilemapLoader.ChunkData>();

        private int _selectedIndex = -1;

        // 工作副本
        private TilemapLoader.ChunkData _working;

        // 强制的块尺寸
        private int _chunkWidth = 32;
        private int _chunkHeight = 32;
        private bool _useSceneChunkSize = true;

        // 绘制
        private int _selectedTileId = 0;
        /// <summary>0=主层+阻挡, 1=地表</summary>
        private int _editLayer = 0;

        // 瓷砖数据库
        private TileDatabase _tileDb = null;
        private List<TileBase> _dbTiles = new List<TileBase>();

        // 保存名称
        private string _saveName = "new_chunk";
        private bool _overwrite = false;

        // 简单快照用于撤销全部
        private int[] _snapshotTiles = null;
        private byte[] _snapshotBlocking = null;
        private int[] _snapshotGround = null;

        [MenuItem("Tools/Map/Chunk Template Editor")]
        public static void OpenWindow()
        {
            
                  

            var wnd = GetWindow<ChunkTemplateEditorWindow>("Chunk Template Editor");
            wnd.minSize = new Vector2(600, 320);
            wnd.LoadTemplates();
            wnd.LoadTileDatabase();
            wnd.RefreshSceneChunkSize();
        }

        private void OnEnable()
        {
          //  ChunkParser.Example();
            LoadTemplates();
            LoadTileDatabase();
            RefreshSceneChunkSize();
        }

        private void LoadTemplates()
        {
            _templateAssets.Clear();
            _templateDatas.Clear();
            _selectedIndex = -1;
            _working = null;

            var assets = Resources.LoadAll<TextAsset>(ResourcesPath);
            if (assets == null) return;
            foreach (var ta in assets)
            {
                if (ta == null) continue;
                _templateAssets.Add(ta);
                try
                {
                    // 清理文本：移除 BOM 和 JS 风格注释，使其兼容 JsonUtility
                    string raw = ta.text ?? string.Empty;
                    raw = raw.TrimStart('\uFEFF');
                    string sanitized = Regex.Replace(raw, @"//.*?$", "", RegexOptions.Multiline);
                    sanitized = Regex.Replace(sanitized, @"/\*.*?\*/", "", RegexOptions.Singleline);
                    sanitized = sanitized.Trim();

                    var data = JsonUtility.FromJson<TilemapLoader.ChunkData>(sanitized);
                    if (data == null) data = new TilemapLoader.ChunkData();

                    // 回退：如果 tiles 缺失或长度不对，尝试手动从文本中提取数字
                    try
                    {
                        // 如果 JsonUtility 未能解析 width/height，尝试从字符串中提取
                        int width = data.width;
                        int height = data.height;
                        if (width <= 0 || height <= 0)
                        {
                            var mw = Regex.Match(sanitized, @"""width""\s*:\s*(\d+)", RegexOptions.Singleline);
                            var mh = Regex.Match(sanitized, @"""height""\s*:\s*(\d+)", RegexOptions.Singleline);
                            if (mw.Success) int.TryParse(mw.Groups[1].Value, out width);
                            if (mh.Success) int.TryParse(mh.Groups[1].Value, out height);
                            if (width > 0 && height > 0)
                            {
                                data.width = width; data.height = height;
                            }
                        }

                        int expected = data.width * data.height;
                        if (expected > 0)
                        {
                            bool needTiles = data.tiles == null || data.tiles.Length != expected;
                            bool needBlocking = data.blocking == null || data.blocking.Length != expected;
                            bool needGround = data.ground == null || data.ground.Length != expected;
                            if (needTiles || needBlocking || needGround)
                            {
                                if (needTiles)
                                    data.tiles = ChunkParser.ParseTilesFallback(sanitized, expected);
                                if (needBlocking)
                                    data.blocking = ChunkParser.ParseByteArrayFallback(sanitized, "blocking", expected);
                                if (needGround)
                                    data.ground = ChunkParser.ParseIntArrayFallback(sanitized, "ground", expected);
                                Debug.Log($"ChunkTemplateEditor: fallback parsed template {ta.name} tiles.len={(data.tiles!=null?data.tiles.Length:0)} blocking.len={(data.blocking!=null?data.blocking.Length:0)} ground.len={(data.ground!=null?data.ground.Length:0)} expected={expected}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("ChunkTemplateEditor: fallback parse failed: " + ex.Message);
                    }

                    if (data == null) data = new TilemapLoader.ChunkData();
                    _templateDatas.Add(data);
                }
                catch (Exception ex)
                {
                    // 解析仍失败；加入 null 标记，以便 SelectTemplate 创建空白工作副本
                    Debug.LogWarning($"ChunkTemplateEditor: failed parse template {ta.name}: {ex.Message}\nPreview:\n" + (ta.text ?? string.Empty).Substring(0, Math.Min(400, (ta.text ?? string.Empty).Length)));
                    _templateDatas.Add(null);
                }
            }

            // 方便起见自动选中第一个有效模板
            for (int i = 0; i < _templateDatas.Count; i++)
            {
                if (_templateDatas[i] != null)
                {
                    SelectTemplate(i);
                    _selectedIndex = i;
                    break;
                }
            }
        }

        private void LoadTileDatabase()
        {
            _tileDb = GetTileDatabase();
            _dbTiles.Clear();
            if (_tileDb != null)
            {
                // 尝试通过 GetTileById 构建 tiles 列表
                int id = 1;
                while (true)
                {
                    try
                    {
                        var tile = _tileDb.GetTileById(id);
                        if (tile == null) break;
                        _dbTiles.Add(tile);
                        id++;
                        if (id > 10000) break; // 安全上限
                    }
                    catch
                    {
                        break;
                    }
                }
            }
        }

        private TileDatabase GetTileDatabase()
        {
            try
            {
                var guids = AssetDatabase.FindAssets("t:TileDatabase");
                if (guids == null || guids.Length == 0) return null;
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                return AssetDatabase.LoadAssetAtPath<TileDatabase>(path);
            }
            catch
            {
                return null;
            }
        }

        private void RefreshSceneChunkSize()
        {
            // 尝试在场景中查找 TilemapManager
            var mgr = UnityEngine.Object.FindObjectOfType<Script.Managers.TilemapManager>();
            if (mgr != null)
            {
                try
                {
                    // 尝试读取序列化属性以避免访问私有字段
                    var so = new SerializedObject(mgr);
                    var wProp = so.FindProperty("chunkWidth");
                    var hProp = so.FindProperty("chunkHeight");
                    if (wProp != null && hProp != null)
                    {
                        _chunkWidth = wProp.intValue;
                        _chunkHeight = hProp.intValue;
                        _useSceneChunkSize = true;
                        return;
                    }
                }
                catch { }

                // 回退：尝试读取公有字段
                try
                {
                    var t = mgr.GetType();
                    var wi = t.GetField("chunkWidth");
                    var hi = t.GetField("chunkHeight");
                    if (wi != null && hi != null)
                    {
                        _chunkWidth = (int)wi.GetValue(mgr);
                        _chunkHeight = (int)hi.GetValue(mgr);
                        _useSceneChunkSize = true;
                        return;
                    }
                }
                catch { }
            }

            // 默认允许手动输入
            _useSceneChunkSize = false;
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal();
            DrawLeftPanel();
            DrawRightPanel();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawLeftPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(240));

            if (GUILayout.Button("Refresh Templates"))
            {
                LoadTemplates();
            }

            _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll);
            for (int i = 0; i < _templateAssets.Count; i++)
            {
                var ta = _templateAssets[i];
                var cd = _templateDatas[i];
                string label = ta != null ? ta.name : "(null)";
                if (cd != null && !string.IsNullOrEmpty(cd.templateName)) label = cd.templateName + " [" + label + "]";

                var style = (i == _selectedIndex) ? EditorStyles.boldLabel : EditorStyles.miniButton;
                var rect = GUILayoutUtility.GetRect(new GUIContent(label), style);

                // 用 Box 画外观，避免 GUI.Button 吞掉右键事件
                GUI.Box(rect, label, style);

                var e = Event.current;
                if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
                {
                    if (e.button == 0)
                    {
                        SelectTemplate(i);
                        e.Use();
                    }
                    else if (e.button == 1)
                    {
                        ShowTemplateContextMenu(i);
                        e.Use();
                    }
                }
            }
            EditorGUILayout.EndScrollView();

            GUILayout.Space(6);
            EditorGUILayout.LabelField("Tile Database:", EditorStyles.boldLabel);
            if (_tileDb == null)
            {
                EditorGUILayout.HelpBox("未在项目中找到 TileDatabase。请通过 Create->Map->TileDatabase 创建。", MessageType.Warning);
                if (GUILayout.Button("Refresh TileDatabase")) LoadTileDatabase();
            }
            else
            {
                EditorGUILayout.LabelField("Database: " + _tileDb.name);
                if (GUILayout.Button("Refresh TileDatabase")) LoadTileDatabase();
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndVertical();
        }

        private void ShowTemplateContextMenu(int index)
        {
            var ta = (index >= 0 && index < _templateAssets.Count) ? _templateAssets[index] : null;
            if (ta == null) return;

            var path = AssetDatabase.GetAssetPath(ta);
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent("Reveal in Project"), false, () =>
            {
                EditorGUIUtility.PingObject(ta);
                Selection.activeObject = ta;
            });

            menu.AddSeparator("");

            menu.AddItem(new GUIContent("Delete"), false, () =>
            {
                bool ok = EditorUtility.DisplayDialog(
                    "Delete Template",
                    $"确定删除模板资源？\n\n{path}\n\n此操作会删除对应的 .json 资产（以及 Unity 的 .meta）。",
                    "Delete",
                    "Cancel"
                );
                if (!ok) return;

                // 如果正在编辑同一个模板，先清掉工作副本，避免保留已删除的引用。
                if (index == _selectedIndex)
                {
                    _selectedIndex = -1;
                    _working = null;
                }

                if (!AssetDatabase.DeleteAsset(path))
                {
                    EditorUtility.DisplayDialog("Delete Failed", $"删除失败：{path}", "OK");
                    return;
                }

                AssetDatabase.Refresh();
                LoadTemplates();
                Repaint();
            });

            menu.ShowAsContext();
        }

        private void SelectTemplate(int index)
        {
            _selectedIndex = index;
            var src = _templateDatas[index];
            if (src == null)
            {
                _working = new TilemapLoader.ChunkData();
                _working.width = _chunkWidth;
                _working.height = _chunkHeight;
                int len = _chunkWidth * _chunkHeight;
                _working.tiles = new int[len];
                _working.blocking = new byte[len];
                _working.ground = new int[len];
                _saveName = _templateAssets[index] != null ? _templateAssets[index].name : "new_chunk";

                Debug.Log($"ChunkTemplateEditor: Selected template index {index} is null -> created blank working (chunk {_chunkWidth}x{_chunkHeight}).");
                return;
            }

            // 深拷贝到工作副本
            _working = new TilemapLoader.ChunkData();
            _working.width = src.width;
            _working.height = src.height;
            int total = _working.width * _working.height;
            if (src.tiles != null) _working.tiles = (int[])src.tiles.Clone(); else _working.tiles = new int[total];
            if (src.blocking != null) _working.blocking = (byte[])src.blocking.Clone(); else _working.blocking = new byte[total];
            if (src.ground != null) _working.ground = (int[])src.ground.Clone(); else _working.ground = new int[total];
            _working.templateName = src.templateName;
            _working.originX = src.originX;
            _working.originY = src.originY;

            // 调试信息
            try
            {
                int nonZero = 0; for (int i = 0; i < _working.tiles.Length; i++) if (_working.tiles[i] != 0) nonZero++;
                string sample = "";
                for (int i = 0; i < Math.Min(8, _working.tiles.Length); i++) sample += _working.tiles[i] + ",";
                Debug.Log($"ChunkTemplateEditor: Selected template {index} ({_saveName}) size {_working.width}x{_working.height} tiles.len={_working.tiles.Length} nonZero={nonZero} sample={sample}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("ChunkTemplateEditor: error while logging template data: " + ex.Message);
            }

            _saveName = !string.IsNullOrEmpty(_working.templateName) ? _working.templateName : (_templateAssets[index] != null ? _templateAssets[index].name : "new_chunk");

            // 如果工作副本尺寸与强制尺寸匹配则保持，否则提示
            if (_useSceneChunkSize)
            {
                // 如果尺寸不同，保持工作副本尺寸，但用户在保存前需修复
            }

            // 快照用于撤销全部
            SnapshotCurrent();
        }

        private void SnapshotCurrent()
        {
            if (_working == null) return;
            _snapshotTiles = (int[])_working.tiles.Clone();
            if (_working.blocking != null) _snapshotBlocking = (byte[])_working.blocking.Clone(); else _snapshotBlocking = null;
            if (_working.ground != null) _snapshotGround = (int[])_working.ground.Clone(); else _snapshotGround = null;
        }

        private void RestoreSnapshot()
        {
            if (_working == null || _snapshotTiles == null) return;
            _working.tiles = (int[])_snapshotTiles.Clone();
            if (_snapshotBlocking != null) _working.blocking = (byte[])_snapshotBlocking.Clone();
            if (_snapshotGround != null) _working.ground = (int[])_snapshotGround.Clone();
        }

        private void EnsureGroundArray(int expected)
        {
            if (_working == null || expected <= 0) return;
            if (_working.ground == null)
                _working.ground = new int[expected];
            else if (_working.ground.Length != expected)
            {
                var nb = new int[expected];
                Array.Copy(_working.ground, nb, Math.Min(_working.ground.Length, expected));
                _working.ground = nb;
            }
        }

        private void DrawRightPanel()
        {
            EditorGUILayout.BeginVertical();

            // 顶部：块尺寸控制
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Chunk Size:", GUILayout.Width(80));
            if (_useSceneChunkSize)
            {
                EditorGUILayout.LabelField($"{_chunkWidth} x {_chunkHeight} (from TilemapManager)");
                if (GUILayout.Button("Refresh from Scene", GUILayout.Width(140))) RefreshSceneChunkSize();
                if (GUILayout.Button("Use Manual", GUILayout.Width(90))) _useSceneChunkSize = false;
            }
            else
            {
                int nw = EditorGUILayout.IntField(_chunkWidth, GUILayout.Width(60));
                int nh = EditorGUILayout.IntField(_chunkHeight, GUILayout.Width(60));
                if (nw != _chunkWidth || nh != _chunkHeight)
                {
                    _chunkWidth = Math.Max(1, nw);
                    _chunkHeight = Math.Max(1, nh);
                }
                if (GUILayout.Button("Refresh from Scene", GUILayout.Width(140))) RefreshSceneChunkSize();
                if (GUILayout.Button("Use Scene", GUILayout.Width(90))) { RefreshSceneChunkSize(); }
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            // 左：格子
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));

            if (_working == null)
            {
                EditorGUILayout.HelpBox("在左侧选择一个模板或者点击 Refresh 从 Resources/chunk_templates 加载模板。", MessageType.Info);
            }
            else
            {
                // 显示元数据
                EditorGUILayout.BeginHorizontal();
                _working.templateName = EditorGUILayout.TextField("Template Name", _working.templateName);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField($"Origin: {_working.originX}, {_working.originY}");

                // 在 UI 显示调试信息
                try
                {
                    int tlen = _working.tiles != null ? _working.tiles.Length : 0;
                    int nonZero = 0;
                    string sample = "";
                    if (_working.tiles != null)
                    {
                        for (int i = 0; i < _working.tiles.Length; i++) if (_working.tiles[i] != 0) nonZero++;
                        for (int i = 0; i < Math.Min(8, _working.tiles.Length); i++) sample += _working.tiles[i] + ",";
                    }
                    EditorGUILayout.LabelField($"Debug: tiles.len={tlen} nonZero={nonZero} sample={sample}");
                }
                catch { }

                // 尺寸不匹配警告
                if (_working.width != _chunkWidth || _working.height != _chunkHeight)
                {
                    EditorGUILayout.HelpBox($"模板尺寸 {_working.width}x{_working.height} 与当前强制尺寸 {_chunkWidth}x{_chunkHeight} 不一致。保存前请点击 'Resize to Chunk Size' 或 'Crop/Pad'.", MessageType.Warning);
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Resize to Chunk Size")) ResizeWorkingToChunkSize();
                    if (GUILayout.Button("Crop/Pad")) CropPadWorkingToChunkSize();
                    EditorGUILayout.EndHorizontal();
                }

                // 编辑层切换
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("编辑层:", GUILayout.Width(50));
                EditorGUI.BeginChangeCheck();
                _editLayer = GUILayout.Toolbar(_editLayer, new[] { "主层+阻挡", "地表" });
                if (EditorGUI.EndChangeCheck()) Repaint();
                EditorGUILayout.EndHorizontal();

                string tip = _editLayer == 0
                    ? "主层+阻挡：左键绘制 Tile ID，右键切换阻挡。阻挡显示红色/B。"
                    : "地表：左键绘制地表 Tile ID，右键清空(0)。";
                EditorGUILayout.HelpBox(tip, MessageType.Info);

                _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);

                int w = _working.width;
                int h = _working.height;
                EnsureGroundArray(w * h);

                // 网格
                for (int y = 0; y < h; y++)
                {
                    EditorGUILayout.BeginHorizontal();
                    for (int x = 0; x < w; x++)
                    {
                        int idx = y * w + x;
                        GUIStyle btnStyle = new GUIStyle(GUI.skin.button) {fixedWidth = 28, fixedHeight = 20};
                        string lbl;
                        var prevColor = GUI.backgroundColor;

                        if (_editLayer == 0)
                        {
                            int id = 0;
                            if (_working.tiles != null && idx < _working.tiles.Length) id = _working.tiles[idx];
                            byte b = 0;
                            if (_working.blocking != null && idx < _working.blocking.Length) b = _working.blocking[idx];
                            lbl = id == 0 ? (b != 0 ? "B" : "-") : id.ToString();
                            if (b != 0) GUI.backgroundColor = Color.red * 0.6f + Color.white * 0.4f;
                        }
                        else
                        {
                            int gid = 0;
                            if (_working.ground != null && idx < _working.ground.Length) gid = _working.ground[idx];
                            lbl = gid == 0 ? "-" : gid.ToString();
                            if (gid != 0) GUI.backgroundColor = new Color(0.6f, 0.85f, 0.6f); // 地表浅绿
                        }

                        if (GUILayout.Button(lbl, btnStyle))
                        {
                            var e = Event.current;
                            if (_editLayer == 0)
                            {
                                if (e.button == 0)
                                    _working.tiles[idx] = _selectedTileId;
                                else if (e.button == 1)
                                {
                                    if (_working.blocking == null) _working.blocking = new byte[w * h];
                                    _working.blocking[idx] = (byte)(_working.blocking[idx] == 0 ? 1 : 0);
                                }
                            }
                            else
                            {
                                if (e.button == 0)
                                    _working.ground[idx] = _selectedTileId;
                                else if (e.button == 1)
                                    _working.ground[idx] = 0;
                            }
                        }
                        GUI.backgroundColor = prevColor;
                    }
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndScrollView();

                GUILayout.Space(6);

                // 工具栏
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Brush Tile ID:", GUILayout.Width(80));
                _selectedTileId = EditorGUILayout.IntField(_selectedTileId, GUILayout.Width(60));
                if (_tileDb != null)
                {
                    if (GUILayout.Button("Pick From DB")) ShowTilePicker();
                }
                if (GUILayout.Button("Fill")) FillWorking(_selectedTileId);
                if (GUILayout.Button("Clear")) ClearWorking();
                if (GUILayout.Button("Undo Changes")) RestoreSnapshot();
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(6);

                // 保存区
                EditorGUILayout.BeginHorizontal();
                _saveName = EditorGUILayout.TextField("Save Name", _saveName);
                _overwrite = EditorGUILayout.ToggleLeft("Overwrite if exists", _overwrite, GUILayout.Width(140));
                if (GUILayout.Button("Save", GUILayout.Width(60))) SaveCurrent();
                if (GUILayout.Button("Save As New", GUILayout.Width(100))) SaveAsNew();
                if (GUILayout.Button("Save As", GUILayout.Width(100))) SaveAs();
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();

            // 右：调色板
            EditorGUILayout.BeginVertical(GUILayout.Width(200));
            EditorGUILayout.LabelField("Palette", EditorStyles.boldLabel);
            if (_tileDb == null)
            {
                EditorGUILayout.HelpBox("未找到 TileDatabase.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.LabelField($"Tiles: {_dbTiles.Count}");
                EditorGUILayout.BeginScrollView(Vector2.zero, GUILayout.Height(400));
                for (int i = 0; i < _dbTiles.Count; i++)
                {
                    var t = _dbTiles[i];
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button(i == (_selectedTileId - 1) ? t.name + " *" : t.name, GUILayout.Width(140)))
                    {
                        _selectedTileId = i + 1;
                    }
                    EditorGUILayout.LabelField((i + 1).ToString(), GUILayout.Width(30));
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndScrollView();
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();


            EditorGUILayout.EndVertical();
        }

        private void ResizeWorkingToChunkSize()
        {
            if (_working == null) return;
            int w = _chunkWidth;
            int h = _chunkHeight;
            var newTiles = new int[w * h];
            var newBlocking = new byte[w * h];
            var newGround = new int[w * h];
            for (int y = 0; y < Math.Min(_working.height, h); y++)
            {
                for (int x = 0; x < Math.Min(_working.width, w); x++)
                {
                    int sIdx = y * _working.width + x;
                    int dIdx = y * w + x;
                    newTiles[dIdx] = _working.tiles != null && sIdx < _working.tiles.Length ? _working.tiles[sIdx] : 0;
                    newBlocking[dIdx] = _working.blocking != null && sIdx < _working.blocking.Length ? _working.blocking[sIdx] : (byte)0;
                    newGround[dIdx] = _working.ground != null && sIdx < _working.ground.Length ? _working.ground[sIdx] : 0;
                }
            }
            _working.width = w;
            _working.height = h;
            _working.tiles = newTiles;
            _working.blocking = newBlocking;
            _working.ground = newGround;
        }

        private void CropPadWorkingToChunkSize()
        {
            // 与 Resize 相同，但保留左上区域
            ResizeWorkingToChunkSize();
        }

        private void FillWorking(int id)
        {
            if (_working == null) return;
            int len = _working.width * _working.height;
            if (_editLayer == 0)
            {
                for (int i = 0; i < len; i++) _working.tiles[i] = id;
            }
            else
            {
                EnsureGroundArray(len);
                for (int i = 0; i < len; i++) _working.ground[i] = id;
            }
        }

        private void ClearWorking()
        {
            if (_working == null) return;
            int len = _working.width * _working.height;
            for (int i = 0; i < len; i++) _working.tiles[i] = 0;
            if (_working.blocking != null) for (int i = 0; i < len; i++) _working.blocking[i] = 0;
            if (_working.ground != null) for (int i = 0; i < len; i++) _working.ground[i] = 0;
        }

        private void ShowTilePicker()
        {
            // 简单弹出菜单列出数据库瓷砖
            var menu = new GenericMenu();
            for (int i = 0; i < _dbTiles.Count; i++)
            {
                int id = i + 1;
                menu.AddItem(new GUIContent($"{id}: {_dbTiles[i].name}"), _selectedTileId == id, () => { _selectedTileId = id; Repaint(); });
            }
            menu.ShowAsContext();
        }

        private void SaveAs()
        {
            if (_working == null) return;
            if (!ValidateAndFixWorkingForSave()) return;

            string fileName = _saveName;
            if (string.IsNullOrWhiteSpace(fileName)) fileName = "new_chunk";
            string relDir = "Assets/Resources/" + ResourcesPath;
            if (!Directory.Exists(relDir)) Directory.CreateDirectory(relDir);
            string path = Path.Combine(relDir, fileName + ".json");
            if (!_overwrite)
            {
                path = AssetDatabase.GenerateUniqueAssetPath(path);
            }
            else
            {
                // ensure unique-ish
                path = path.Replace("\\", "/");
            }

            SaveWorkingToPath(path, allowOverwrite: _overwrite);
        }

        private void SaveCurrent()
        {
            if (_working == null) return;
            if (!ValidateAndFixWorkingForSave()) return;

            if (_selectedIndex < 0 || _selectedIndex >= _templateAssets.Count || _templateAssets[_selectedIndex] == null)
            {
                EditorUtility.DisplayDialog("Save Current", "当前没有选中的模板文件。请先在左侧选择一个模板，或使用 Save As New / Save As。", "OK");
                return;
            }

            var ta = _templateAssets[_selectedIndex];
            var path = AssetDatabase.GetAssetPath(ta);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("Save Current", $"当前选中的模板不是可保存的 json 资源：{path}", "OK");
                return;
            }

            SaveWorkingToPath(path, allowOverwrite: true);
        }

        private void SaveAsNew()
        {
            if (_working == null) return;
            if (!ValidateAndFixWorkingForSave()) return;

            string fileName = _saveName;
            if (string.IsNullOrWhiteSpace(fileName)) fileName = "new_chunk";
            string relDir = "Assets/Resources/" + ResourcesPath;
            if (!Directory.Exists(relDir)) Directory.CreateDirectory(relDir);
            string path = Path.Combine(relDir, fileName + ".json");
            path = AssetDatabase.GenerateUniqueAssetPath(path);
            path = path.Replace("\\", "/");

            SaveWorkingToPath(path, allowOverwrite: false);
        }

        private bool ValidateAndFixWorkingForSave()
        {
            int expected = _working.width * _working.height;
            if (_working.tiles == null || _working.tiles.Length != expected)
            {
                EditorUtility.DisplayDialog("Invalid", $"tiles 数组长度应为 {expected}", "OK");
                return false;
            }
            if (_working.blocking == null)
                _working.blocking = new byte[expected];
            else if (_working.blocking.Length != expected)
            {
                var nb = new byte[expected];
                Array.Copy(_working.blocking, nb, Math.Min(_working.blocking.Length, expected));
                _working.blocking = nb;
            }
            EnsureGroundArray(expected);
            return true;
        }

        private void SaveWorkingToPath(string path, bool allowOverwrite)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            // 统一为 AssetDatabase 路径格式
            path = path.Replace("\\", "/");

            if (!allowOverwrite)
            {
                path = AssetDatabase.GenerateUniqueAssetPath(path);
            }

            try
            {
                string json = JsonUtility.ToJson(_working, true);
                File.WriteAllText(path, json);
                AssetDatabase.ImportAsset(path);
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("Saved", "Template wrote to: " + path, "OK");

                // 重新加载模板并选中新保存的
                LoadTemplates();
                var newTa = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                if (newTa != null)
                {
                    for (int i = 0; i < _templateAssets.Count; i++)
                    {
                        if (_templateAssets[i] == newTa)
                        {
                            SelectTemplate(i);
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("Failed to save template: " + ex);
                EditorUtility.DisplayDialog("Error", "Failed to save template: " + ex.Message, "OK");
            }
        }
    }
}
