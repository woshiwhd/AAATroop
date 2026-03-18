using UnityEngine;
using Script.Utilities;
using Script.AI;

namespace Script.Player
{
    /// <summary>
    /// PlayerController：一个简单的 2D 角色控制器（适用于 tilemap 场景）
    /// 功能：
    /// - 使用箭头键 / WASD 控制角色移动
    /// - 纯逻辑移动（直接修改 transform），无物理推挤
    /// - 为 Animator 提供 MoveX/MoveY/Speed 参数（如果存在 Animator）
    /// - 实现 ICombatUnit，用于被敌人/其他单位作为战斗目标。
    /// </summary>
    public class PlayerController : MonoBehaviour, ICombatUnit
    {
        [Header("Movement")]
        [Tooltip("移动速度（单位：格/秒或单位/秒，取决于 Tile 大小和场景缩放）")]
        public float moveSpeed = 5f;

        [Tooltip("用于逻辑碰撞的半径（米）。如果为 0 将尝试从 CircleCollider2D 自动推导。")]
        public float collisionRadius = 0.2f;

        [Tooltip("阻挡层的 LayerMask，只检测这些层（通常是阻挡 Tilemap 所在层）。")]
        public LayerMask blockingLayerMask;

        [Tooltip("与墙体保持的安全距离（皮肤宽度），避免卡进碰撞体内部。")]
        public float skinWidth = 0.02f;

        [Header("Components (optional)")]
        public Animator animator; // 可选：用于播放行走/站立动画

        [Header("Combat")]
        [Tooltip("玩家所属阵营")]
        public Faction faction = Faction.Player;

        private static readonly int MoveXHash = Animator.StringToHash("MoveX");
        private static readonly int MoveYHash = Animator.StringToHash("MoveY");
        private static readonly int SpeedHash = Animator.StringToHash("Speed");

        private Vector2 _input;

        void Update()
        {
            float hx = Input.GetAxisRaw("Horizontal");
            float hy = Input.GetAxisRaw("Vertical");

            _input = new Vector2(hx, hy);
            if (_input.sqrMagnitude > 1f) _input = _input.normalized;

            Vector2 moveDelta = _input * moveSpeed * Time.deltaTime;
            TryMove(moveDelta);

            if (animator != null)
            {
                animator.SetFloat(MoveXHash, _input.x);
                animator.SetFloat(MoveYHash, _input.y);
                animator.SetFloat(SpeedHash, _input.sqrMagnitude);
            }
        }

        /// <summary>供外部调用的瞬移（例如定位角色到某个 tile 中心）</summary>
        public void TeleportTo(Vector2 worldPosition)
        {
            transform.position = new Vector3(worldPosition.x, worldPosition.y, transform.position.z);
        }

        /// <summary>
        /// 尝试按给定位移移动玩家，在移动前进行 Physics2D 碰撞检测，避免穿过阻挡层。
        /// </summary>
        private void TryMove(Vector2 delta)
        {
            if (delta == Vector2.zero)
                return;

            // 推导半径：优先使用配置的 collisionRadius，其次尝试从 CircleCollider2D 读取。
            float radius = collisionRadius;
            if (radius <= 0f)
            {
                var circle = GetComponent<CircleCollider2D>();
                if (circle != null)
                    radius = Mathf.Max(circle.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.y), 0.01f);
                else
                    radius = 0.2f;
            }

            Vector2 origin = transform.position;
            Vector2 direction = delta.normalized;
            float distance = delta.magnitude;

            // 若未配置阻挡层，默认检测所有层，方便早期调试。
            LayerMask mask = blockingLayerMask.value == 0 ? Physics2D.DefaultRaycastLayers : blockingLayerMask;

            // 使用 CircleCast 检测从当前位置到目标位置之间是否有阻挡。
            RaycastHit2D hit = Physics2D.CircleCast(origin, radius, direction, distance + skinWidth, mask);

            if (hit.collider == null)
            {
                // 没有命中阻挡，直接完整移动。
                transform.position = new Vector3(origin.x + delta.x, origin.y + delta.y, transform.position.z);
                return;
            }

            // 命中阻挡：计算可以安全移动的最大距离（留出 skinWidth）。
            float allowed = Mathf.Max(0f, hit.distance - skinWidth);
            if (allowed <= 0f)
                return;

            Vector2 move = direction * allowed;
            transform.position = new Vector3(origin.x + move.x, origin.y + move.y, transform.position.z);
        }

        #region ICombatUnit 实现
        public Transform Transform => transform;

        public Faction Faction => faction;

        // 目前暂不实现生命系统，先始终视为存活；后续可接入 HP。
        public bool IsAlive => true;

        public void TakeDamage(float amount)
        {
            // 后续可在此实现玩家受伤与死亡逻辑。
            GameLog.Log($"Player TakeDamage: {amount}");
        }
        #endregion
    }
}
