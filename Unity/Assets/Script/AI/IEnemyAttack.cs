using UnityEngine;

namespace Script.AI
{
    /// <summary>
    /// IEnemyAttack：敌人攻击行为接口
    /// - 提供 CanAttack 与 DoAttack 的抽象，允许不同攻击实现插拔
    /// </summary>
    public interface IEnemyAttack
    {
        /// <summary>
        /// 判断是否在可攻击范围（实现可以考虑朝向、遮挡等）
        /// </summary>
        bool CanAttack(Transform self, Transform target, float attackRange);

        /// <summary>
        /// 执行一次攻击（播放动画/造成伤害）
        /// </summary>
        void DoAttack(Transform self, Transform target);
    }
}
