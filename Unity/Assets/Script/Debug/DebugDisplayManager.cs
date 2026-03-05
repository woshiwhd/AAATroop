using UnityEngine;

namespace Script.Debug
{
    /// <summary>
    /// 统一控制 Scene 中所有辅助显示（EnemyDebugDraw、ChunkDebugDraw 等）的开关。
    /// 挂在任意 GameObject（如 Managers）上，在 Inspector 中勾选即可全局开关。
    /// </summary>
    [ExecuteAlways]
    public class DebugDisplayManager : MonoBehaviour
    {
        /// <summary>全局开关：false 时所有 DebugDraw 组件均不绘制。</summary>
        public static bool EnableAllDebugDraws { get; private set; } = true;

        [Header("辅助显示")]
        [Tooltip("勾选时绘制敌人/Chunk 等调试 Gizmos")]
        [SerializeField] private bool enableDebugDraws = true;

        private void OnEnable()
        {
            EnableAllDebugDraws = enableDebugDraws;
        }

        private void OnValidate()
        {
            EnableAllDebugDraws = enableDebugDraws;
        }
    }
}
