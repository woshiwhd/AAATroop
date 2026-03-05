# Enemy AI 检查（Visual Scripting）

> 敌人 AI 由 **Visual Scripting** 实现，无 C# 驱动逻辑；EnemyHelper 仅提供可调用的感知与行为方法。

## Main 场景 enemy 节点现状（已核对）

- **GameObject**：name = `enemy`，Tag = `enemy`，父节点为 Entities。
- **组件**：Transform、SpriteRenderer、Rigidbody2D、CircleCollider2D、Animator、**Script Machine**（引用宏/图）、**Scene Variables**、**EnemyHelper**。
- **EnemyHelper**：config 指向 `Assets/BT/EnemyConfig.asset`；**player 已改为指向 Player 的 Transform**（原错误指向 enemy 自身 Transform，已修正）；patrolPoints 为空。
- **行为图**：Script Machine 引用的宏/图为 `EnemyStateGraph`（State Graph），包含状态 idle、Chase 与过渡条件 playerTargeted。

### 行为图逻辑与需在编辑器中改的两处

1. **idle 状态**  
   - 每帧 **Update → GetComponent(EnemyHelper) → Invoke(CanSeePlayer)**。  
   - 问题：**Invoke(CanSeePlayer) 的返回值没有接到 Set Variable(playerSeen)**，所以变量 `playerSeen` 从未被赋值为 `true`，过渡条件 `playerSeen == true` 永远不成立，不会进入 Chase。  
   - **请在 Visual Scripting 里**：把 Invoke(CanSeePlayer) 的 **result/return 端口** 连到 **Set Variable** 的 **value**，变量名保持 `playerSeen`。

2. **Chase 状态**  
   - 目前只有 OnEnterState、Update、OnExitState 三个节点，**Update 下没有接任何移动或调用**，所以进入 Chase 后敌人不会动。  
   - **请在 Chase 的 Update 里**：接上对 EnemyHelper 的调用，例如 **GetComponent(EnemyHelper) → Invoke(MoveToPlayerNow)**（或先取 player 的 position 再 Invoke MoveTowards(targetPos, deltaTime)），这样才会朝玩家移动。

以上两处只能在 Unity 编辑器的 Visual Scripting 图中修改，无法用脚本自动改。

---

## 一、当前结构概览

```
Enemy GameObject
├── EnemyHelper      ← 配置(config)、玩家(player)、感知/行为方法(CanSeePlayer, MoveToPlayerNow, DoAttack…)
├── (可选) IEnemyMovement 实现  ← EnemyHelper 优先用此做移动
├── (可选) IEnemyAttack 实现    ← EnemyHelper 优先用此做攻击
├── Rigidbody2D      ← BT_MoveToUnit 会用到
├── Animator         ← BT_PlayAnimUnit 会用到
└── Visual Scripting 图（挂到该对象或通过 Event/Graph 驱动）
```

- **EnemyHelper**：提供「是否看见玩家 / 是否可攻击 / 朝目标移动 / 执行攻击」等，用 **EnemyConfig**（sightRange、attackRange、speed 等）和 **player**。
- **BT_* Units**：在 Visual Scripting 里用的节点，做「找最近目标、是否在范围内、移动、播动画」，**不直接读 EnemyHelper / EnemyConfig**。

---

## 二、BT 节点说明

| 节点 | 作用 | 输入 | 输出/分支 |
|------|------|------|-----------|
| **BT/Find Nearest Target** | 按 tag 找最近 GameObject（如 "Player"） | self, tag | result (GameObject)，outSuccess / outFailure |
| **BT/Is In Range** | 判断 self 与 target 距离 ≤ range | self, target, range | outSuccess / outFailure |
| **BT/Move To** | 朝 targetPos 移动（Rigidbody2D 或 transform） | self, targetPos, speed | out |
| **BT/Play Anim** | 对 self 的 Animator 打 SetTrigger | self, trigger | out |

- **Find Nearest Target**：内部用 `FindGameObjectsWithTag`，建议在图中**节流**（例如 0.2～0.5s 调一次），避免每帧全场景搜。
- **Move To**：直接改 `Rigidbody2D.velocity` 或 `transform`，**不会**走 EnemyHelper 的 `MoveTowards`，也不会用 **EnemyConfig.speed**，除非你在图里把 speed 从别处（如变量/常量）接到该节点。
- **Is In Range**：range 需要自己在图里提供（例如从 **EnemyHelper.config.attackRange** 或 Script 变量读）。

---

## 三、EnemyHelper 与 BT 的衔接方式

- **EnemyHelper** 提供：`CanSeePlayer()`、`CanAttack()`、`MoveToPlayerNow()`、`AttackOnce()`、巡逻相关等，都依赖 **config** 和 **player**。
- **BT 节点** 不包含「从 self 取 EnemyHelper 再调这些方法」的逻辑，所以有两种用法：

1. **以 BT 为主（找目标 + 距离 + 移动 + 动画）**  
   - 用 **BT/Find Nearest Target** → **BT/Is In Range** → **BT/Move To** / **BT/Play Anim** 做流程。  
   - 此时 **sightRange / attackRange / speed** 不会自动从 EnemyConfig 来，需要你在图里：  
     - 要么用 **Invoke** / 自定义节点从 `self.GetComponent<EnemyHelper>().config` 读再传给 BT 的 range、speed；  
     - 要么用 Script 变量/Blackboard 存这些值并在图里连线。

2. **以 EnemyHelper 为主（用 Invoke 调方法）**  
   - 用 Visual Scripting 的 **Invoke**（或自定义 Unit）在 self 上调用：  
     - `EnemyHelper.CanSeePlayer` / `CanAttack`（做条件分支）  
     - `EnemyHelper.MoveToPlayerNow` / `AttackOnce`（做移动和攻击）  
   - 这样会自然用到 **EnemyConfig** 和 **IEnemyMovement / IEnemyAttack**。

也可以**混合**：BT 负责「找目标 + 距离判断 + 播动画」，**攻击生效**（扣血等）通过 Invoke `EnemyHelper.AttackOnce()` 或你的伤害系统。

---

## 四、建议的图流程示例（BT 为主）

1. **每帧或节流**：  
   **BT/Find Nearest Target** (self, tag = "Player")  
   - outFailure → 可接「巡逻」或闲置。  
   - outSuccess → 取 **result** 作为 target。

2. **有 target 时**：  
   **BT/Is In Range** (self, target, **range**)  
   - 这里的 **range** 建议从 EnemyHelper.config.attackRange 或 Script 变量来（否则要手填常量）。  
   - outSuccess（在攻击范围内）→ **BT/Play Anim** (self, "Attack")，并可 **Invoke** `EnemyHelper.AttackOnce()` 做实际伤害。  
   - outFailure（不在范围内）→ **BT/Move To** (self, **target.transform.position**, **speed**)。  
   - **speed** 建议从 EnemyHelper.config.speed 或变量来，否则不会用配置。

3. **Player 的 Tag**：  
   确保场景里玩家根节点或需要被「找最近」的对象 **Tag = "Player"**，否则 Find Nearest Target 会一直 outFailure。

---

## 五、常见问题与核对清单

| 现象 | 可能原因 | 建议 |
|------|----------|------|
| 敌人不追玩家 | 没找到 target（Tag 不是 "Player"）或 Find 没节流导致卡/不执行 | 检查 Player Tag；Find 用 Timer 或 Delta Time 节流 |
| 敌人不移动 | BT/Move To 的 targetPos 没接 target 的 position，或 speed=0 | 用 Find 的 result → result.transform.position 接 Move To；speed 接 config 或变量 |
| 从不攻击 | 攻击范围判断没接 config.attackRange，或从未走「在范围内」分支 | Is In Range 的 range 接 EnemyHelper.config.attackRange 或等同变量；确认 outSuccess 接到 Play Anim + 攻击逻辑 |
| 没用上 EnemyConfig | BT 节点不读 EnemyHelper | 用 Invoke/变量把 config.sightRange、attackRange、speed 传入 BT，或改用 Invoke 调 EnemyHelper 方法 |
| 移动很怪 / 不用 Rigidbody | 敌人没有 Rigidbody2D | 给敌人加 Rigidbody2D；BT_MoveToUnit 会优先用 velocity |

---

## 六、小结

- **EnemyHelper**：提供与 **EnemyConfig**、**player**、**IEnemyMovement / IEnemyAttack** 绑定的感知与行为方法，适合用 **Invoke** 或自定义 Unit 调用。
- **BT_* 节点**：提供「找目标、判距、移动、播动画」的通用流程，但 **不自动读 EnemyHelper/EnemyConfig**；要在图里把 **range / speed** 从 config 或变量接进去，或改用 Invoke 走 EnemyHelper。
- 建议：要么**图里显式接 config 的 attackRange、speed** 到 BT，要么**用 Invoke 调 EnemyHelper** 做感知与行为，这样行为树和配置/接口才一致。

---

## 七、实现方式说明

当前实现：**Visual Scripting State Graph（状态机） + EnemyHelper（行为 API）**，而不是经典的行为树（Selector/Sequence 节点树）。从设计和可维护性看，大致如下。

### 优点

| 方面 | 说明 |
|------|------|
| **流程与实现分离** | 状态流转在 State Graph 里，具体“怎么看见/怎么移动/怎么攻击”在 EnemyHelper 和接口里，改逻辑不必改 C#，改数值只动 EnemyConfig。 |
| **配置驱动** | EnemyConfig（ScriptableObject）集中 sightRange、attackRange、speed 等，换配置即可换敌人参数，适合多兵种/多关卡。 |
| **可插拔行为** | IEnemyMovement / IEnemyAttack 让移动、攻击可替换（例如 Rigidbody 移动、射线攻击），不绑死一种实现。 |
| **策划/非程序可调** | 状态、过渡条件、调用的方法名都在图里，策划能改“什么时候追、什么时候打”，无需改代码。 |
| **与 Unity 生态一致** | 用官方 Visual Scripting，不引入第三方 BT 框架，维护成本低。 |

### 缺点与风险

| 方面 | 说明 |
|------|------|
| **图易出错、难排查** | 像“CanSeePlayer 返回值没接到 Set Variable”这类问题没有编译报错，只能跑起来看或逐节点检查；连线一多可读性下降。 |
| **两套“行为入口”** | 既有 EnemyHelper（Invoke 调方法），又有 BT_* 节点（找目标、判距、移动）；图里若混用，容易出现“一部分用 config、一部分用图里常量”，行为不一致。 |
| **性能与调用频率** | 每帧 GetComponent + Invoke 有开销；若用 BT/Find Nearest Target 每帧找玩家，成本更高，需要自己在图里做节流。 |
| **版本与协作** | State Graph 是资产，合并冲突、diff 不如代码直观；多人改图要约定好谁改哪一块。 |
| **状态机表达能力** | 纯状态机适合“明确状态 + 明确转换”的 AI；若以后要做“优先级、打断、子树复用”，会不如经典 BT 或带栈的状态机灵活。 |

### 适用场景

- **适合**：中小规模敌人种类、逻辑以“idle → 发现 → 追击 → 攻击”为主、希望策划能调状态与条件、团队接受用 Visual Scripting 调参和改流程。
- **不太适合**：大量复杂分支、需要经典行为树式的优先级与打断；此时可考虑在 Visual Scripting 中重构状态/子图或引入更多 BT 风格节点。

### 可选改进方向

1. **统一数据源**：图里只用 EnemyHelper 的 Invoke（CanSeePlayer、MoveToPlayerNow、AttackOnce），或只用 BT 节点但**所有 range/speed 都从 EnemyHelper.config 或 Blackboard 读**，避免图里写死常量。
2. **图内注释与检查清单**：在文档或图旁维护一份“每个状态的必连端口”（如 idle：CanSeePlayer → Set Variable），减少漏连。
3. **节流与性能**：需要“找玩家”时用 Timer 或 Delta Time 节流，避免每帧 Find 或每帧多次 GetComponent；重复用到的组件可缓存到变量。

**总结**：这种方式在“状态清晰、配置与接口分离、策划可参与”方面做得不错，适合当前规模的敌人 AI；主要风险在图的可读性、连线的正确性和“两套行为入口”的统一。建议要么**彻底以 EnemyHelper 为主**（图只做状态 + Invoke），要么**以 BT 节点为主**并统一从 config/Blackboard 取参，二选一并坚持，会更好维护。
