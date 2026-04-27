using System.Collections.Generic;
using UnityEngine;

namespace Script.Managers
{
    /// <summary>
    /// 特殊地形遭遇：将 chunk 坐标映射到 Resources 模板文件名（与 ChunkTemplateLoader.GetTemplateCopyByName 一致）。
    /// 由关卡/战斗逻辑在运行时 Register，TilemapManager 加载该 chunk 时优先使用模板而非噪声。
    /// </summary>
    public class SpecialChunkEncounterRegistry : MonoBehaviour
    {
        private readonly Dictionary<Vector2Int, string> _chunkToTemplateName = new Dictionary<Vector2Int, string>();

        public void RegisterEncounterChunk(Vector2Int chunk, string templateAssetName)
        {
            if (string.IsNullOrEmpty(templateAssetName)) return;
            _chunkToTemplateName[chunk] = templateAssetName;
        }

        public void UnregisterChunk(Vector2Int chunk)
        {
            _chunkToTemplateName.Remove(chunk);
        }

        public bool TryGetTemplateName(Vector2Int chunk, out string templateName)
        {
            return _chunkToTemplateName.TryGetValue(chunk, out templateName);
        }

        public void ClearAll()
        {
            _chunkToTemplateName.Clear();
        }
    }
}
