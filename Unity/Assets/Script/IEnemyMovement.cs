using UnityEngine;

namespace Script.AI
{
    /// <summary>
    /// IEnemyMovement：敌人移动行为接口
    /// - 实现该接口可以插入自定义移动逻辑（例如基于 NavMeshAgent）
    /// - EnemyHelper 会优先使用该接口的实现（通过 GetComponent）
    /// </summary>
    public interface IEnemyMovement
    {
        /// <summary>
        /// 移动到目标位置（由 EnemyHelper 调用）
        /// </summary>
        /// <param name="targetPos">目标世界坐标</param>
        /// <param name="deltaTime">当前帧间隔（Time.deltaTime）</param>
        /// <param name="speed">移动速度（来自配置）</param>
        void MoveTowards(Vector3 targetPos, float deltaTime, float speed);
    }
}
