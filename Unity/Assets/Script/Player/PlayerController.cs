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
            transform.position += new Vector3(moveDelta.x, moveDelta.y, 0f);

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
