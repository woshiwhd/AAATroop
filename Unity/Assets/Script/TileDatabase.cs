using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Script
{
    [CreateAssetMenu(fileName = "TileDatabase", menuName = "Map/TileDatabase")]
    public class TileDatabase : ScriptableObject
    {
        [SerializeField] private List<TileBase> tiles = new List<TileBase>();

        // Get id for tile. Return 0 for null or not found. IDs start at 1.
        /// <summary>
        /// 获取瓦片对应的 id（从 1 开始）。
        /// - 返回 0 表示空瓦片或未找到。
        /// </summary>
        public int GetTileId(TileBase tile)
        {
            if (tile == null) return 0;
            int idx = tiles.IndexOf(tile);
            if (idx < 0) return 0;
            return idx + 1;
        }

        // Get TileBase by id. id 0 => null
        /// <summary>
        /// 根据 id 获取 TileBase（id 从 1 开始），id=0 返回 null。
        /// </summary>
        public TileBase GetTileById(int id)
        {
            if (id <= 0) return null;
            int idx = id - 1;
            if (idx >= tiles.Count) return null;
            return tiles[idx];
        }

    #if UNITY_EDITOR
        // Editor helper to ensure tile is present and returns its id.
        /// <summary>
        /// 编辑器辅助：如果缺少该瓦片则添加并保存 Asset。仅在编辑器模式下可用。
        /// </summary>
        public int AddTileIfMissing(TileBase tile)
        {
            if (tile == null) return 0;
            int idx = tiles.IndexOf(tile);
            if (idx >= 0) return idx + 1;
            tiles.Add(tile);
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.AssetDatabase.SaveAssets();
            return tiles.Count; // new id
        }
    #endif
    }
}
