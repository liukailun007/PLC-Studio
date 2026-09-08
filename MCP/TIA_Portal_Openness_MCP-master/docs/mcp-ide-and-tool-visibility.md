# MCP 与 IDE：交付包边界说明

交付包 **不绑定** 某一种 IDE（Cursor / VS Code / Claude Desktop / 自建 HTTP 客户端均可）。  
协议只有两类：**stdio**（子进程 JSON-RPC）与 **HTTP**（完整 MCP 会话），见 `tools/tiaportal-mcp/skill/SKILL.md` §2。

## 1. 工具列表以谁为准

| 来源 | 作用 |
|------|------|
| **`manifest/tools-list.json`** | 离线快照：便于检索、文档交叉引用、CI 校验。 |
| **运行中的 `TiaMcpServer` 返回的 `tools/list`** | **权威**：以实际加载的程序集为准，名称/参数可能与快照有细微差异。 |

任何 IDE 只要正确挂上 MCP Server，都应能枚举到 **完整** 工具集（例如 `PlcBuildAndImport`、`ConnectDeviceNodesToProfinetSubnet` 等）。

## 2. 「IDE 裁剪」指什么（不是交付包缺陷）

部分 IDE 在 **MCP 插件侧** 只把 **磁盘上预置的 JSON 描述符** 暴露给模型，用于自动补全/校验；若该描述符 **未与服务器同步更新**，模型侧会看到 **工具缺失**，而 **同一 exe 在其它客户端或 CLI 仍可调全量工具**。

**处理办法（任选）：**

1. 用交付包内 **`manifest/tools-list.json`** 与 **`docs/tool-capability-matrix.md`** 对照真实需求；  
2. 在客户端中 **刷新 / 重新注册** MCP Server，使描述符与当前 `TiaMcpServer.exe` 一致；  
3. 直接对运行中的 Server 调 **`tools/list`**，把结果固化为团队内部「工具白名单」文档。

## 3. 与交付包文档的关系

- `README.md`、`full-project-generation-runbook.md` 中的工具名以 **交付包内 manifest + SKILL** 为准编写。  
- 若你使用的 IDE 显示「无此工具」，**优先怀疑客户端描述符滞后**，而不是怀疑交付包未包含该能力。
