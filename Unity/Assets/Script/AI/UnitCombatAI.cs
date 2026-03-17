using UnityEngine;
using System.Collections.Generic;
using Script.AI;
using Script.Player;
using Script.Utilities;

namespace Script
{
    /// <summary>
    /// UnitCombatAI：通用的战斗单位 AI 控制器。
    /// - 暴露常用方法给 Visual Scripting 使用（例如 CanSeeTarget、IsTargetInAttackRange、MoveToTargetNow、AttackCurrentTarget）
    /// - 支持通过 ScriptableObject 配置（UnitCombatConfig）和通过接口注入具体的移动/攻击实现
    /// - 设计目标：逻辑与实现分离，Visual Scripting 只控制状态流程，具体行为在这里实现或由其他组件实现
    /// </summary>
    [DisallowMultipleComponent]
    public class UnitCombatAI : MonoBehaviour
    {
        [Header("配置")]
        public UnitCombatConfig config;

        [Header("阵营与目标")]
        [Tooltip("当前单位所属阵营")]
        public Faction faction = Faction.Enemy;

        [Tooltip("可选：初始锁定的目标 Transform（将尝试从中获取 ICombatUnit）")]
        public Transform initialTarget;

        [Header("巡逻点（可选）")]
        public List<Transform> patrolPoints = new List<Transform>();

        // runtime cached
        private IEnemyMovement _movementImpl;
        private IEnemyAttack _attackImpl;
        private ICombatUnit _selfUnit;
        private ICombatUnit _currentTarget;

        // 简单的内部状态，方便 VS 调用无参方法
        private int _currentPatrolIndex = 0;

        /// <summary>当前锁定的战斗目标（只读）。</summary>
        public ICombatUnit CurrentTarget => _currentTarget;

        void Awake()
        {
            // 尝试获取可插拔实现
            _movementImpl = GetComponent<IEnemyMovement>();
            _attackImpl = GetComponent<IEnemyAttack>();
            _selfUnit = GetComponent<ICombatUnit>();

            TryInitializeDefaultTarget();
        }

        #region 感知与判断（供 Visual Scripting 调用）
        /// <summary>
        /// 当前是否有有效目标（存在、存活、带 Transform）。
        /// </summary>
        public bool HasTarget()
        {
            return _currentTarget != null &&
                   _currentTarget.IsAlive &&
                   _currentTarget.Transform != null;
        }

        /// <summary>
        /// 判断是否能看到当前目标（简单距离判断）。
        /// </summary>
        public bool CanSeeTarget()
        {
            if (!HasTarget() || config == null) return false;

            var targetPos = _currentTarget.Transform.position;
            float dist = Vector3.Distance(transform.position, targetPos);
            return dist <= config.sightRange;
        }

        /// <summary>
        /// 判断目标是否在攻击范围内（优先使用 IEnemyAttack 的判断）。
        /// </summary>
        public bool IsTargetInAttackRange()
        {
            if (!HasTarget() || config == null) return false;

            var targetTransform = _currentTarget.Transform;
            if (_attackImpl != null)
            {
                return _attackImpl.CanAttack(transform, targetTransform, config.attackRange);
            }

            float dist = Vector3.Distance(transform.position, targetTransform.position);
            return dist <= config.attackRange;
        }

        // 为兼容现有 VS 图，保留旧命名的包装方法。
        public bool CanSeePlayer() => CanSeeTarget();
        public bool CanAttack() => IsTargetInAttackRange();
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
            dir.z = 0f; // 保持在同一深度，适合当前 2D 场景
            if (dir.sqrMagnitude < 0.0001f) return;
            transform.position += dir.normalized * config.speed * deltaTime;
            transform.rotation = Quaternion.LookRotation(dir.normalized);
        }

        /// <summary>
        /// 朝当前目标移动（由 Visual Scripting 在 Update/State 中调用）。
        /// </summary>
        public void MoveToTargetNow()
        {
            if (!HasTarget()) return;
            MoveTowards(_currentTarget.Transform.position, Time.deltaTime);
        }

        /// <summary>
        /// 执行一次对当前目标的攻击（由 Visual Scripting 调用触发）。
        /// </summary>
        public void AttackCurrentTarget()
        {
            if (!HasTarget()) return;

            if (_attackImpl != null)
            {
                _attackImpl.DoAttack(transform, _currentTarget.Transform);
                return;
            }
            // 默认实现：打印日志并可以在此触发 Animator
            GameLog.Log($"{name} AttackCurrentTarget");
        }

        // 旧 API 的兼容包装。
        public void DoAttack() => AttackCurrentTarget();

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
        /// 把单位移动到当前目标位置（使用 Time.deltaTime）
        /// </summary>
        public void MoveToPlayerNow()
        {
            MoveToTargetNow();
        }

        /// <summary>
        /// 对当前目标执行一次攻击（包装 AttackCurrentTarget）
        /// </summary>
        public void AttackOnce()
        {
            AttackCurrentTarget();
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

        #region 目标选择
        /// <summary>
        /// 在配置的视野范围内尝试寻找一个新的目标。
        /// </summary>
        public void AcquireTargetInRange()
        {
            if (config == null) return;
            AcquireTarget(config.sightRange);
        }

        /// <summary>
        /// 在给定半径内寻找最近的敌对 ICombatUnit 作为目标。
        /// </summary>
        public void AcquireTarget(float radius)
        {
            if (radius <= 0f) return;
            var hits = Physics2D.OverlapCircleAll(transform.position, radius);
            ICombatUnit best = null;
            float bestSqrDist = float.MaxValue;

            foreach (var hit in hits)
            {
                if (hit == null) continue;
                var unit = hit.GetComponentInParent<ICombatUnit>();
                if (unit == null) continue;
                if (unit == _selfUnit) continue;
                if (!unit.IsAlive) continue;
                if (unit.Faction == faction) continue;

                Vector3 diff = unit.Transform.position - transform.position;
                float sqrDist = diff.sqrMagnitude;
                if (sqrDist < bestSqrDist)
                {
                    bestSqrDist = sqrDist;
                    best = unit;
                }
            }

            _currentTarget = best;
        }

        public void ClearTarget()
        {
            _currentTarget = null;
        }
        #endregion

        private void TryInitializeDefaultTarget()
        {
            if (initialTarget != null)
            {
                _currentTarget = initialTarget.GetComponentInParent<ICombatUnit>();
                return;
            }
 
        }
    }
}

