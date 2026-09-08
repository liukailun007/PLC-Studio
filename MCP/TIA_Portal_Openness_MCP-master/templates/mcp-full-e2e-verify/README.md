# MCP Full E2E Verify（验证素材）

本目录提供 **LAD / SCL / Tag** 等导入样例，用于在**已有工程**里验证 MCP 导入与编译链路，与 **`full_plc_hmi_project.json` 蓝图生成的主工程**相互独立。

## 与 skill cookbook 的关系

| 位置 | 用途 |
|------|------|
| `templates/mcp-full-e2e-verify/` | 打包验收：相对路径固定，便于脚本或文档引用 |
| `tools/tiaportal-mcp/skill/lad-cookbook/`、`scl-cookbook/` | SKILL 文档中的同名示例源；内容应与验证块保持一致策略 |

导入 **`ImportBlock`** / **`ImportPlcExternalSource`** 时请使用 **绝对路径**。同一 FC/FB **勿重复导入同名**，否则报名称冲突（可先删外部源或块再导入）。

## 建议顺序（PLC）

1. Tag 表（若有 `import/*.xml`）  
2. `plc/blocks/` 下 XML（按依赖：先 FC，含实例的再 FB）  
3. `plc/scl/*.scl`：外部源导入后 **`GenerateBlocksFromExternalSource`**  
4. **`CompileAndDiagnosePlc`**

详见根目录 **`tools/tiaportal-mcp/skill/SKILL.md`** §9–§11（LAD）、§10（SCL DSL）、§14（外部 SCL）。
