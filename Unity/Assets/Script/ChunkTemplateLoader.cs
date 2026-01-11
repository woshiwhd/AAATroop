using System.Collections.Generic;
using UnityEngine;
using System.Text.RegularExpressions;

namespace Script
{
    /// <summary>
    /// 简单的模板加载器：从 Resources/chunk_templates 加载若干 chunk 模板（JSON -> TilemapLoader.ChunkData），
    /// 并提供随机获取模板的能力。所有加载与解析在主线程执行（适合在 Awake 中初始化）。
    /// 模板应当包含 width/height/tiles（row-major），且模板尺寸应与运行时的 chunkWidth/chunkHeight 相同。
    /// </summary>
    public class ChunkTemplateLoader
    {
        private readonly List<TilemapLoader.ChunkData> _templates = new List<TilemapLoader.ChunkData>();
        // 会扫描的资源路径列表（Resources 下的子目录），优先包含 chunk_templates 以及 兼容性的 chunks
        private readonly string[] _resourcesPaths = new[] { "chunk_templates", "chunks" };
        private bool _debug = false;

        /// <summary>
        /// 在主线程初始化，扫描 Resources/chunk_templates 下的 TextAsset，解析为 ChunkData。
        /// 仅接受与 expectedWidth/expectedHeight 匹配的模板。
        /// </summary>
        public void Initialize(int expectedWidth, int expectedHeight, bool debug = false)
        {
            _debug = debug;
            _templates.Clear();

            int totalLoaded = 0;
            foreach (var path in _resourcesPaths)
             {
                var assets = Resources.LoadAll<TextAsset>(path);
                if (assets == null || assets.Length == 0)
                {
                    if (_debug) Debug.Log($"ChunkTemplateLoader: 在 Resources/{path} 未发现任何 TextAsset。");
                    continue;
                }

                foreach (var ta in assets)
                {
                    if (ta == null) continue;
                    try
                    {
                        // 原始文本
                        string raw = ta.text ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(raw))
                        {
                            if (_debug) Debug.LogWarning($"ChunkTemplateLoader: 资源内容为空: {ta.name}");
                            continue;
                        }

                        // 先尝试直接解析（最快路径）
                        TilemapLoader.ChunkData chunk = null;
                        try
                        {
                            chunk = JsonUtility.FromJson<TilemapLoader.ChunkData>(raw);
                        }
                        catch (System.ArgumentException)
                        {
                            // 交由下面的容错流程处理
                        }

                        // 如果直接解析失败，尝试对 JSON 做常见问题的预处理后再解析（去 BOM、注释、尾随逗号等）
                        if (chunk == null)
                        {
                            string sanitized = SanitizeJson(raw);
                            if (!ReferenceEquals(sanitized, raw))
                            {
                                if (_debug) Debug.Log($"ChunkTemplateLoader: 尝试对 {ta.name} 进行 JSON 预处理并重新解析。预处理后前200字符:\n{GetPreview(sanitized,200)}");
                            }

                            try
                            {
                                chunk = JsonUtility.FromJson<TilemapLoader.ChunkData>(sanitized);
                            }
                            catch (System.ArgumentException ex)
                            {
                                // 解析仍失败，打印详细提示与片段，帮助用户定位 JSON 问题
                                if (_debug)
                                {
                                    Debug.LogWarning($"ChunkTemplateLoader: 解析模板出错 {ta.name}: {ex}\n-- 文件内容预览（前1000字符） --\n{GetPreview(raw,1000)}\n-- 建议：检查 JSON 是否为有效的 Unity JsonUtility 支持格式（使用双引号、无尾随逗号、无注释），并确认字段名匹配 ChunkData 的定义。\n");
                                }
                                continue;
                            }
                        }

                        if (chunk == null)
                        {
                            if (_debug) Debug.LogWarning($"ChunkTemplateLoader: 解析模板失败 (null): {ta.name}");
                            continue;
                        }

                        if (chunk.width != expectedWidth || chunk.height != expectedHeight)
                        {
                            if (_debug) Debug.LogWarning($"ChunkTemplateLoader: 模板尺寸不匹配 ({ta.name}): {chunk.width}x{chunk.height} != expected {expectedWidth}x{expectedHeight}");
                            continue;
                        }

                        _templates.Add(chunk);
                        totalLoaded++;
                    }
                    catch (System.Exception ex)
                    {
                        if (_debug) Debug.LogWarning($"ChunkTemplateLoader: 解析模板出错 {ta.name}: {ex}");
                    }
                }
             }

            if (_debug) Debug.Log($"ChunkTemplateLoader: 已加载 {_templates.Count} 个模板 (总计 {totalLoaded} 文件)。");
        }

        /// <summary>
        /// 返回模板的深拷贝，方便调用方修改 origin 等字段而不影响缓存。
        /// 如果没有可用模板返回 null。
        /// </summary>
        public TilemapLoader.ChunkData GetRandomTemplateCopy()
        {
            if (_templates.Count == 0) return null;
            var t = _templates[Random.Range(0, _templates.Count)];
            var copy = new TilemapLoader.ChunkData();
            copy.width = t.width;
            copy.height = t.height;
            if (t.tiles != null) copy.tiles = (int[])t.tiles.Clone();
            if (t.blocking != null) copy.blocking = (byte[])t.blocking.Clone();
            copy.originX = t.originX;
            copy.originY = t.originY;
            return copy;
        }

        // 辅助：去除 BOM、注释（//、/* */）以及 JSON 中常见的尾随逗号问题
        private static string SanitizeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;

            // 去掉 UTF-8 BOM
            if (s.Length > 0 && s[0] == '\uFEFF') s = s.Substring(1);

            // 移除单行注释 // ... (注意：简单实现，不能识别字符串内的 //)
            s = Regex.Replace(s, @"//.*(?=\r?$)", string.Empty, RegexOptions.Multiline);
            // 移除块注释 /* ... */
            s = Regex.Replace(s, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            // 移除对象或数组内部的尾随逗号（例如 { "a":1, "b":2, } -> { "a":1, "b":2 }）
            s = Regex.Replace(s, @"([{\[,])\s*([^,\]}]+)\s*([,\}])", "$1$2$3");

            return s;
        }

        // 辅助：获取字符串的前 N 个字符预览，便于调试日志
        private static string GetPreview(string s, int maxLength)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            if (s.Length <= maxLength) return s;
            return s.Substring(0, maxLength) + "...";
        }
     }
}
