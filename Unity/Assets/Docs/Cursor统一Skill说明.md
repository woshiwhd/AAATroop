# Cursor 统一 Skill 说明

## 背景

本项目已新增项目级 Skill：`unity-game-dev`。  
Skill 文件位置：`.cursor/skills/unity-game-dev/SKILL.md`。

该 Skill 用于统一 Unity 2D 开发过程中的代码实现、地图生成、场景与预制体改动规范，降低多人协作时风格和质量不一致的问题。

## 适用场景

- 新增或修改 `Manager`、`Generator`、`Registry` 等核心脚本
- 调整 Tilemap、Chunk、程序化生成相关逻辑
- 修改 Scene/Prefab 并涉及脚本字段或组件依赖
- 进行代码评审、重构、问题修复时需要统一标准

## 主要约束（摘要）

1. 变更要聚焦，避免把无关重构混入同一任务。
2. 优先扩展现有流程，不轻易新增全局系统。
3. 保持序列化安全，避免破坏 Inspector 数据与资源引用。
4. 程序化生成在提供种子时应可复现。
5. 地图写入前应校验 tile key / tile ID，避免非法数据。

## 使用方式

1. 在 Cursor 中直接描述开发任务（如“修改 TilemapManager 的生成策略”）。
2. Agent 会根据触发语义自动应用 `unity-game-dev` Skill。
3. 完成后建议按统一格式汇报：
   - 改了什么
   - 为什么改
   - 做了哪些验证
   - 风险与后续建议

## 维护建议

- 规范更新时，优先修改 `SKILL.md` 作为单一事实来源。
- 如需补充示例，可新增 `examples.md` 并在 `SKILL.md` 中链接。
- 若后续引入自动化测试，可把测试命令写入 Skill，提升任务闭环效率。
