using UnityEngine;

namespace Script
{
    /// <summary>
    /// GroundManager 已简化为仅支持 Tilemap 流程。
    /// - useTilemap = true 时会禁用同 GameObject 上的 SpriteRenderer，避免与 Tilemap 冲突。
    /// - 不再包含 Sprite 平铺逻辑（由 TilemapManager 处理）。
    /// </summary>
    public class GroundManager : MonoBehaviour
    {
        [SerializeField] private bool useTilemap = true;

        void Awake()
        {
            if (!useTilemap) return;

            // 如果场景里挂有 SpriteRenderer，禁用它，确保 Tilemap 渲染正常
            var sr = GetComponent<SpriteRenderer>();
            if (sr != null && sr.enabled)
            {
                sr.enabled = false;
            }
        }
    }
}
