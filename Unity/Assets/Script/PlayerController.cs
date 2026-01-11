using UnityEngine;

namespace Script
{
    /// <summary>
    /// PlayerController：一个简单的 2D 角色控制器（适用于 tilemap 场景）
    /// 功能：
    /// - 使用箭头键 / WASD 控制角色移动
    /// - 基于 Rigidbody2D 的物理移动（推荐）
    /// - 为 Animator 提供 MoveX/MoveY/Speed 参数（如果存在 Animator）
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Movement")]
        [Tooltip("移动速度（单位：格/秒或单位/秒，取决于 Tile 大小和场景缩放）")]
        public float moveSpeed = 5f;

        [Header("Components (optional) ")]
        public Rigidbody2D rb; // 建议在 Inspector 指定，若为空将在 Awake 尝试获取
        public Animator animator; // 可选：用于播放行走/站立动画

        // Animator 参数哈希（避免字符串参数的性能与静态分析警告）
        private static readonly int MoveXHash = Animator.StringToHash("MoveX");
        private static readonly int MoveYHash = Animator.StringToHash("MoveY");
        private static readonly int SpeedHash = Animator.StringToHash("Speed");

        // 运行时缓存
        private Vector2 _input;
        private Vector2 _moveDelta;

        void Awake()
        {
            if (rb == null) rb = GetComponent<Rigidbody2D>();
            if (rb == null)
            {
                Debug.LogError("PlayerController: 需要 Rigidbody2D 组件，请在 GameObject 上添加。");
                enabled = false;
                return;
            }

            // 推荐设置（不强制修改）：确保 Rigidbody2D 的 Body Type = Dynamic，使用合适的 Collision Detection
            // 例如：rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        void Update()
        {
            // 读取原始轴输入（不做平滑），支持键盘箭头和 WASD
            float hx = Input.GetAxisRaw("Horizontal");
            float hy = Input.GetAxisRaw("Vertical");

            // 优先八方向但可保持对角移动
            _input = new Vector2(hx, hy);
            if (_input.sqrMagnitude > 1f) _input = _input.normalized; // 防止对角速度过快

            _moveDelta = _input * moveSpeed;

            // 更新 Animator 参数（如果有），使用哈希以提高效率
            if (animator != null)
            {
                animator.SetFloat(MoveXHash, _input.x);
                animator.SetFloat(MoveYHash, _input.y);
                animator.SetFloat(SpeedHash, _input.sqrMagnitude);
            }
        }

        void FixedUpdate()
        {
            // 使用 Rigidbody2D.MovePosition 以获得稳定的物理移动与碰撞响应
            if (rb != null)
            {
                Vector2 newPos = rb.position + _moveDelta * Time.fixedDeltaTime;
                rb.MovePosition(newPos);
            }
        }

        // 供外部调用的瞬移（例如定位角色到某个 tile 中心）
        public void TeleportTo(Vector2 worldPosition)
        {
            if (rb != null)
            {
                rb.position = worldPosition;
                rb.velocity = Vector2.zero;
            }
            else
            {
                transform.position = worldPosition;
            }
        }
    }
}
