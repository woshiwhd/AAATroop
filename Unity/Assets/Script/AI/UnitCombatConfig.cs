using UnityEngine;

namespace Script.AI
{
    /// <summary>
    /// UnitCombatConfig：战斗单位参数配置（ScriptableObject）
    /// - 可调参数集中在此，不同单位可复用不同配置资源
    /// - 供 UnitCombatAI、Visual Scripting 等使用
    /// </summary>
    [CreateAssetMenu(menuName = "AAATroop/UnitCombatConfig", fileName = "UnitCombatConfig")]
    public class UnitCombatConfig : ScriptableObject
    {
        [Header("感知与战斗")]
        [Tooltip("视野距离（单位：世界单位）")]
        public float sightRange = 10f;

        [Tooltip("攻击距离（小于等于视为可攻击）")]
        public float attackRange = 2f;

        [Tooltip("追踪丢失后回到巡逻的等待时间（秒）")]
        public float loseTime = 3f;

        [Tooltip("攻击冷却（秒）")]
        public float attackCooldown = 1f;

        [Header("移动")]
        [Tooltip("移动速度（单位：世界单位/秒）")]
        public float speed = 3f;
    }
}
