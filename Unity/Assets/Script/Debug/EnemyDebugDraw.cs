using UnityEngine;
using Script;

namespace Script.Debug
{
    /// <summary>
    /// 编辑器中绘制敌人调试信息（视野、攻击范围、巡逻点、到玩家的连线）。
    /// 挂在带 EnemyHelper 的敌人上；仅 Scene 视图可见。
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(EnemyHelper))]
    public class EnemyDebugDraw : MonoBehaviour
    {
        [Header("显示选项")]
        [Tooltip("仅选中时绘制（否则始终绘制）")]
        public bool drawOnlyWhenSelected = true;

        private void OnDrawGizmos()
        {
            if (drawOnlyWhenSelected) return;
            DrawInternal();
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawOnlyWhenSelected) return;
            DrawInternal();
        }

        private void DrawInternal()
        {
            var helper = GetComponent<EnemyHelper>();
            if (helper == null || helper.config == null) return;

            var pos = transform.position;
            float z = pos.z;

            // 视野范围（绿色）
            Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
            DrawCircleXy(pos, helper.config.sightRange, 24, z);

            // 攻击范围（红色）
            Gizmos.color = new Color(1f, 0f, 0f, 0.4f);
            DrawCircleXy(pos, helper.config.attackRange, 16, z);

            // 到玩家的连线（黄色）
            if (helper.player != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(pos, helper.player.position);
            }

            // 巡逻点与路径（青色）
            if (helper.patrolPoints != null && helper.patrolPoints.Count > 0)
            {
                Gizmos.color = Color.cyan;
                var prev = pos;
                foreach (var pt in helper.patrolPoints)
                {
                    if (pt == null) continue;
                    var p = pt.position;
                    Gizmos.DrawLine(prev, p);
                    Gizmos.DrawWireSphere(p, 0.2f);
                    prev = p;
                }
                Gizmos.DrawLine(prev, pos);
            }
        }

        private static void DrawCircleXy(Vector3 center, float radius, int segments, float z)
        {
            float angleStep = 360f / Mathf.Max(segments, 4);
            var prev = center + new Vector3(radius, 0f, 0f);
            prev.z = z;
            for (int i = 1; i <= segments; i++)
            {
                float a = i * angleStep * Mathf.Deg2Rad;
                var next = center + new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f);
                next.z = z;
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }
}
