using UnityEngine;

namespace Script.AI
{
    /// <summary>
    /// 具备战斗能力的单位接口，在 ITargetable 基础上增加受伤等行为。
    /// </summary>
    public interface ICombatUnit : ITargetable
    {
        /// <summary>受到一次伤害。具体数值与死亡逻辑由实现方决定。</summary>
        void TakeDamage(float amount);
    }
}

