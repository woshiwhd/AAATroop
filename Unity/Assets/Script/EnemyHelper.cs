using UnityEngine;
using System.Collections.Generic;
using Script.AI;
using Script.Player;

namespace Script
{
    /// <summary>
    /// EnemyHelper：通用的敌人辅助组件
    /// - 暴露常用方法给 Visual Scripting 使用（例如 CanSeePlayer、CanAttack、MoveTowards、DoAttack）
    /// - 支持通过 ScriptableObject 配置（EnemyConfig）和通过接口注入具体的移动/攻击实现
    /// - 设计目标：逻辑与实现分离，Visual Scripting 只控制状态流程，具体行为在这里实现或由其他组件实现
    /// </summary>
    [DisallowMultipleComponent]
    public class EnemyHelper : MonoBehaviour
    {
        [Header("配置")]
        public EnemyConfig config;

        [Header("引用（可在 Inspector 指定，也可以运行时由系统查找）")]
        public Transform player;

        [Header("巡逻点（可选）")]
        public List<Transform> patrolPoints = new List<Transform>();

        // runtime cached
        private IEnemyMovement _movementImpl;
        private IEnemyAttack _attackImpl;

        // 简单的内部状态，方便 VS 调用无参方法
        private int _currentPatrolIndex = 0;

        void Awake()
        {
            // 尝试获取可插拔实现
            _movementImpl = GetComponent<IEnemyMovement>();
            _attackImpl = GetComponent<IEnemyAttack>();

            // 如果 Player 未指定，尝试自动找到场景内的 PlayerController 或名为 Player 的对象
            if (player == null)
            {
                var pc = FindObjectOfType<PlayerController>();
                if (pc != null) player = pc.transform;
                else
                {
                    var go = GameObject.Find("Player");
                    if (go != null) player = go.transform;
                }
            }
        }

        #region 感知与判断（供 Visual Scripting 调用）
        /// <summary>
        /// 判断是否能看到玩家（简单距离判断）
        /// </summary>
        public bool CanSeePlayer()
        {
            if (player == null || config == null) return false;
            return Vector3.Distance(transform.position, player.position) <= config.sightRange;
        }

        /// <summary>
        /// 判断是否在攻击范围内（优先使用 IEnemyAttack 的判断）
        /// </summary>
        public bool CanAttack()
        {
            if (player == null || config == null) return false;
            if (_attackImpl != null) return _attackImpl.CanAttack(transform, player, config.attackRange);
            return Vector3.Distance(transform.position, player.position) <= config.attackRange;
        }
        #endregion

        #region 行为方法（供 Visual Scripting 调用）
        /// <summary>
        /// 移动到目标位置（由 Visual Scripting 在 Update/State 中调用）
        /// </summary>
        public void MoveTowards(Vector3 targetPos, float deltaTime)
        {
            if (config == null) return;
            if (_movementImpl != null)
            {
                _movementImpl.MoveTowards(targetPos, deltaTime, config.speed);
                return;
            }

            // fallback：直接修改 transform（2D 项目通常适用）
            Vector3 dir = targetPos - transform.position;
            dir.y = 0f; // 保持在同一高度，适合纯 2D
            if (dir.sqrMagnitude < 0.0001f) return;
            transform.position += dir.normalized * config.speed * deltaTime;
            transform.rotation = Quaternion.LookRotation(dir.normalized);
        }

        /// <summary>
        /// 执行一次攻击（由 Visual Scripting 调用触发）
        /// </summary>
        public void DoAttack()
        {
            if (_attackImpl != null)
            {
                _attackImpl.DoAttack(transform, player);
                return;
            }
            // 默认实现：打印日志并可以在此触发 Animator
            Debug.Log($"{name} DoAttack");
        }

        /// <summary>
        /// 获取某个巡逻点的位置（供 Visual Scripting 读取）
        /// </summary>
        public Vector3 GetPatrolPointPosition(int index)
        {
            if (patrolPoints == null || patrolPoints.Count == 0) return transform.position;
            index = Mathf.Clamp(index, 0, patrolPoints.Count - 1);
            return patrolPoints[index].position;
        }
        #endregion

        #region 便捷包装方法（无参或简单参数，便于 Visual Scripting 快速调用）
        /// <summary>
        /// 把敌人移动到玩家位置（使用 Time.deltaTime）
        /// </summary>
        public void MoveToPlayerNow()
        {
            if (player == null) return;
            MoveTowards(player.position, Time.deltaTime);
        }

        /// <summary>
        /// 对玩家执行一次攻击（包装 DoAttack）
        /// </summary>
        public void AttackOnce()
        {
            DoAttack();
        }

        /// <summary>
        /// 获取当前巡逻点位置
        /// </summary>
        public Vector3 GetCurrentPatrolPoint()
        {
            return GetPatrolPointPosition(_currentPatrolIndex);
        }

        /// <summary>
        /// 移动到当前巡逻点
        /// </summary>
        public void MoveToCurrentPatrol()
        {
            MoveTowards(GetCurrentPatrolPoint(), Time.deltaTime);
        }

        /// <summary>
        /// 切换到下一个巡逻点（循环）
        /// </summary>
        public void NextPatrolIndex()
        {
            if (patrolPoints == null || patrolPoints.Count == 0) return;
            _currentPatrolIndex = (_currentPatrolIndex + 1) % patrolPoints.Count;
        }

        /// <summary>
        /// 明确设置巡逻点索引（在 Visual Scripting 中可传入 int 参数）
        /// </summary>
        public void SetPatrolIndex(int idx)
        {
            if (patrolPoints == null || patrolPoints.Count == 0) { _currentPatrolIndex = 0; return; }
            _currentPatrolIndex = Mathf.Clamp(idx, 0, patrolPoints.Count - 1);
        }
        #endregion
    }
}
