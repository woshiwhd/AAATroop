# 地图编辑器（Chunk Template Editor）说明

本文档说明 `Tools/Map/Chunk Template Editor` 的用途、数据来源与使用流程，避免后续误用导致地图数据错乱。

## 一、入口与作用

- **入口**：Unity 菜单 `Tools/Map/Chunk Template Editor`
- **主要用途**：
  - 从 `Resources/chunk_templates` 加载 **JSON 块模板**
  - 编辑每格的 **tile id（int）** 与 **blocking（byte：0/1）**
  - `Save As` 写入 `Assets/Resources/chunk_templates/{name}.json`

实现文件：`Assets/Editor/ChunkTemplateEditorWindow.cs`

---

## 二、模板（Template）从哪里来？

- **Resources 路径**：`Resources/chunk_templates`
- **加载方式**：`Resources.LoadAll<TextAsset>("chunk_templates")`
- **数据结构**：解析为 `TilemapLoader.ChunkData`

说明：模板 JSON 里存的是数组（`tiles[]`、`blocking[]`），编辑器网格里显示的数字就是 `tile id`。

---

## 三、Tile（瓦片）从哪里来？为什么格子里是数字？

Chunk 模板编辑器不直接存 `TileBase` 引用，而是存 **tile id**。`tile id` 的映射来自 `TileDatabase`（ScriptableObject）：

- `TileDatabase.tiles` 是 `List<TileBase>`
- **id 从 1 开始**：第 0 个元素对应 id=1
- **id=0 表示空瓦片（null）**

实现文件：`Assets/Script/Utilities/TileDatabase.cs`

> 重要：模板 JSON 里保存的是数字 id，因此 **TileDatabase 的顺序就是“协议”**。随意插入/排序会导致旧模板全部错位。

---

## 四、Chunk Size（块大小）怎么来的？

编辑器顶部 `Chunk Size` 有两种来源：

- **Use Scene（推荐）**：从当前场景的 `TilemapManager` 读取 `chunkWidth/chunkHeight`
- **Use Manual**：手动输入宽高

当模板的 `width/height` 与当前 chunk size 不一致时，编辑器会提示并提供：

- `Resize to Chunk Size`
- `Crop/Pad`

---

## 五、界面说明与操作

### 1）左侧：模板列表 + TileDatabase 状态

- **Refresh Templates**：重新从 `Resources/chunk_templates` 读取模板
- **Tile Database**：
  - 若提示未找到：在 Project 搜索 `t:TileDatabase`
  - 或通过 `Create -> Map -> TileDatabase` 创建（然后手动把 Tile 拖入 tiles 列表）

### 2）中间：网格编辑区（核心）

按钮显示规则：

- `-`：tile id=0 且不阻挡
- `B`：tile id=0 但阻挡（blocking=1）
- `数字`：tile id（例如 12）

鼠标操作：

- **左键点击格子**：设置该格子的 `tile id = Brush Tile ID`
- **右键点击格子**：切换 `blocking` 0/1（红色背景表示阻挡）

### 3）工具栏

- **Brush Tile ID**：当前画笔 id
- **Pick From DB**：从 TileDatabase 的 tile 列表中挑选（本质是选 id）
- **Fill**：用当前 id 填满整个模板
- **Clear**：全部清空为 0；blocking 清空为 0
- **Undo Changes**：恢复选中模板时的快照（整体撤销）

### 4）右侧：Palette（调色板）

- 右侧列表来自 `TileDatabase.tiles`
- 点击某项会设置 `Brush Tile ID = i + 1`

---

## 六、保存（Save As）与文件落点

- **保存目录**：`Assets/Resources/chunk_templates/`
- **保存文件**：`{Save Name}.json`
- **Overwrite if exists**：
  - 未勾选：使用唯一文件名（自动追加后缀）
  - 勾选：覆盖同名文件（建议配合版本管理谨慎使用）

保存后会自动重新导入资源并刷新模板列表。

---

## 七、新增一个 Tile 应该在哪里加？（强烈建议按此流程）

1. 先创建 Tile 资源（或 RuleTile）：
   - `Create -> 2D -> Tiles -> Tile`（或 RuleTile）
   - 给它绑定 Sprite
2. 再把 Tile **追加**到 `TileDatabase.tiles` 列表末尾

**不要在中间插入/排序**：会导致所有旧模板中的 id 映射错乱。

---

## 八、常见问题（FAQ）

- **Q：为什么我在编辑器里看不到某个 tile？**
  - **A**：检查 `TileDatabase.tiles` 是否包含它；Palette 只来自 TileDatabase。

- **Q：模板放到别的目录为什么不显示？**
  - **A**：编辑器固定从 `Resources/chunk_templates` 加载。

- **Q：为什么格子不显示贴图，只显示数字？**
  - **A**：当前实现只编辑 id/blocking，不做 Tile 贴图预览。
