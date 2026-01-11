using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Tilemaps;

namespace Script
{
    /// <summary>
    /// 主入口类（示例）：处理两个按钮和一个文本显示。
    /// - 为演示用，包含对公开字段的 null 检查，避免在未在 Inspector 赋值时抛异常。
    /// - 另外增加了对常见场景结构的说明和简单运行时检查，帮助确认 Tilemap / Player 的节点是否按行业惯例组织。
    /// 
    /// 推荐的场景结构（业内常见）：
    /// - Root
    /// -  ├─ Managers (存放全局管理脚本如 TilemapManager, ChunkManager 等)
    /// -  ├─ Grid (包含 Grid 组件)
    /// -  │   ├─ Tilemap_Ground (用于绘制地面 Tile)
    /// -  │   └─ Tilemap_Blocking (用于存放阻挡 Tile, 用于寻路/碰撞检测)
    /// -  ├─ Entities (运行时生成/放置的实体，如 Player、NPC)
    /// -  │   └─ Player (角色节点，带 PlayerController)
    ///
    /// 上述结构有利于资源分层、序列化与运行时查找。
    /// </summary>
    public class Main : MonoBehaviour
    {
        [SerializeField] private Button Btn1;
        [SerializeField] private Button Btn2;
        [SerializeField] private Text Txt1;

        // 可选的 Inspector 引用，用于在启动时做简单检查（非必须，若不赋值则不会报错，只会给出提示）
        [Header("Scene Validation (optional)")]
        [SerializeField] private GameObject GridRoot;
        [SerializeField] private GameObject EntitiesRoot;
        [SerializeField] private Tilemap GroundTilemap; // 可用来快速检测 Tilemap 是否存在并被设置
        [SerializeField] private Tilemap BlockingTilemap; // 可选的阻挡层引用
        [SerializeField] private GameObject PlayerObject;

        // Start is called before the first frame update
        void Start()
        {
            // 对序列化字段做 null 检查，若未赋值则记录错误并忽略绑定，防止 NullReferenceException
            if (Btn1 != null) Btn1.onClick.AddListener(OnBtn1Click);
            else Debug.LogError("Main: Btn1 未在 Inspector 中赋值");

            if (Btn2 != null) Btn2.onClick.AddListener(OnBtn2Click);
            else Debug.LogError("Main: Btn2 未在 Inspector 中赋值");

            // 可选的场景结构检查（仅输出帮助信息，不会修改场景）
            ValidateSceneStructure();
        }

        private void ValidateSceneStructure()
        {
            // 尝试自动查找常见节点并赋值，便于快速校验场景是否按推荐结构组织
            // 1) Grid
            if (GridRoot == null)
            {
                var found = GameObject.Find("Grid");
                if (found != null)
                {
                    GridRoot = found;
                    Debug.Log("Main: 自动找到 Grid 节点并赋值到 GridRoot。");
                }
                else
                {
                    Debug.LogWarning("Main: GridRoot 未设置（可选）。建议在场景中放置一个名为 'Grid' 的 GameObject，并将 Tilemap 放在它下面。");
                }
            }

            // 如果有 GridRoot，检查其下是否包含 Grid 组件和 Tilemap
            if (GridRoot != null)
            {
                var gridComp = GridRoot.GetComponent<Grid>();
                if (gridComp == null) Debug.LogWarning("Main: GridRoot 未包含 Grid 组件，建议在 GridRoot 上添加 Grid 组件以便使用 Tilemap。");

                // 尝试寻找 Tilemap_Ground 或任意第一个 Tilemap 作为 GroundTilemap
                if (GroundTilemap == null)
                {
                    var tm = GridRoot.GetComponentInChildren<Tilemap>();
                    if (tm != null)
                    {
                        GroundTilemap = tm;
                        Debug.Log("Main: 在 Grid 下找到 Tilemap 并自动赋值给 GroundTilemap（建议命名为 Tilemap_Ground）。");
                    }
                    else
                    {
                        Debug.LogWarning("Main: 在 GridRoot 下未找到 Tilemap 子节点，建议将 Tilemap (Tilemap_Ground / Tilemap_Blocking) 放在 GridRoot 下。");
                    }
                }

                // 如果 BlockingTilemap 未设置，尝试找名为 Tilemap_Blocking 的子节点
                if (BlockingTilemap == null)
                {
                    var children = GridRoot.GetComponentsInChildren<Tilemap>(true);
                    foreach (var c in children)
                    {
                        if (c.gameObject.name.ToLower().Contains("block") || c.gameObject.name.ToLower().Contains("obstacle") || c.gameObject.name.ToLower().Contains("blocking"))
                        {
                            BlockingTilemap = c;
                            Debug.Log("Main: 在 Grid 下找到可能的阻挡 Tilemap 并赋值给 BlockingTilemap。");
                            break;
                        }
                    }
                }
            }

            // Entities
            if (EntitiesRoot == null)
            {
                var ent = GameObject.Find("Entities");
                if (ent != null)
                {
                    EntitiesRoot = ent;
                    Debug.Log("Main: 自动找到 Entities 节点并赋值到 EntitiesRoot。");
                }
            }

            // Player
            if (PlayerObject == null)
            {
                // 优先在 Entities 下查找名为 Player 的节点
                GameObject found = null;
                if (EntitiesRoot != null)
                {
                    var t = EntitiesRoot.transform.Find("Player");
                    if (t != null) found = t.gameObject;
                }

                // 其次全局查找名为 Player 的节点
                if (found == null) found = GameObject.Find("Player");

                // 最后尝试查找任何包含 PlayerController 组件的对象
                if (found == null)
                {
                    var pc = FindObjectOfType<PlayerController>();
                    if (pc != null) found = pc.gameObject;
                }

                if (found != null)
                {
                    PlayerObject = found;
                    Debug.Log("Main: 自动找到 Player 并赋值到 PlayerObject。");
                }
                else
                {
                    Debug.LogWarning("Main: 未找到 Player 节点（可选）。建议在 Entities 下创建名为 'Player' 的 GameObject 并添加 PlayerController。");
                }
            }

            // 输出一些最终建议和校验提示，帮助快速定位问题
            if (GroundTilemap == null) Debug.LogWarning("Main: GroundTilemap 仍未设置，运行时依赖 Tilemap 的管理脚本可能无法正常工作。请在 Inspector 中手动设置 GroundTilemap 或确保场景中存在 Tilemap。");
            if (PlayerObject == null) Debug.LogWarning("Main: PlayerObject 未设置。若需要玩家控制或运行时实体，请创建并设置 PlayerObject。");
        }

        private void OnBtn1Click()
        {
            if (Txt1 != null) Txt1.text = "Button 1 was clicked!";
            Debug.Log("Btn1 Clicked");
        }
        private void OnBtn2Click()
        {
            if (Txt1 != null) Txt1.text = "Button 2 was clicked!";
            Debug.Log("Btn2 Clicked");
        }
        // （已移除空的 Update 函数以消除冗余事件函数警告）
    }
}
