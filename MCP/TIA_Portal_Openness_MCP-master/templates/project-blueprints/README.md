# Project Blueprints

本目录存放完整项目生成蓝图。

| 文件 | 用途 |
|---|---|
| `full_plc_hmi_project.json` | 创建 PLC + WinCC Unified 项目的主配方，包含硬件、PLC、HMI、按钮动作、动态化和验收项。 |

**自检：** 包根目录运行 `scripts\Validate-Bundle.ps1`，确认 `requiredBundleFiles` 所列路径齐全且 JSON 可解析。

使用规则：

1. 先读取根目录 `README.md`、`tools/tiaportal-mcp/skill/SKILL.md` 和本蓝图。
2. MCP 第一工具为 `Bootstrap`。
3. 所有项目路径、软件路径、设备路径都来自 TIA 读回。
4. PLC 写入先 dryRun，再真实导入。
5. HMI 先建连接和变量，再建画面，最后绑定动作和动态化。
6. 项目保存前必须完成编译和关键对象读回。
