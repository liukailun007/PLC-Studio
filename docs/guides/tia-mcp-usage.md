# TIA Portal MCP (V21) 使用指南

> 面向通过自然语言在 PLC Studio 中驱动 TIA Portal Openness MCP 服务器（约 201 个工具）
> 的开发者。目标：**更精准地选中工具、更高效地开发博途（TIA Portal）项目**，而不是逐个记住工具名。

## 这份指南解决什么问题

TIA MCP 有约 201 个工具，按功能域和分层（L0 / L1 / L2）组织。对自然语言驱动的模型而言，
挑选工具的依据只有两样：

1. 每个工具的 **名称 + description**（MCP `tools/list` 上报的 schema 文本）；
2. 会话历史中你已经交代过的上下文。

所以"调用不精准"几乎总是源于信息缺失，而不是模型"笨"。本指南把模型天生缺的三样东西补齐：

- **路线图** —— 先做什么后做什么；
- **前置条件链** —— 每个操作依赖哪个上游结果；
- **参数来源** —— `softwarePath` / `devicePath` / `blockPath` 这些值从哪里拿，而非靠猜。

## 一句话原则（可直接复述给模型）

> 先探路（Connect → OpenProject → GetProjectTree），再用真实路径操作；
> 所有路径来自树的返回值，禁止臆造；动手前想清楚工具的前置条件与安全边界。

---

## L0 / L1 / L2 分层速览

TIA MCP 的工具自带层级标签。理解分层，模型就很少选错层级：

| 层 | 定位 | 典型工具 |
|---|---|---|
| **L0** | 会话入口 / 自检 / 报告，只读为主 | Connect 前的引导（Bootstrap）、环境 Doctor、连接能力自检、在线监控安全自检、验收 / 错误报告 |
| **L1** | **每次会话的核心流程工具**，必走的主干 | Connect、Disconnect、OpenProject、CreateProject、SaveProject、CloseProject、GetProjectTree、Compile、CompileAndDiagnosePlc、加设备（AddDevice / SearchHardwareCatalog）、GoOnline、DownloadToPlc |
| **L2** | 专项 / 高级 / 离线构建器 | 报警、OPC UA、技术对象、在线监控（S7 / OPC UA 直读）、HMI Classic / Unified、全局库、离线 XML 构建器（PLC-Builders）、反射（Reflection）、报告 / 校验套件 |

**经验法则**：能走 L1 就别去 L2 拼细节；L2 里名字以 `Build*` / `Compose*` 结尾的
"离线构建器"只返回 XML，**不连接 TIA、不导入、不改项目**——它们是给后续
`ImportBlock` / `ImportType` 喂料的中间步骤，不是最终操作。

---

## 主干工作流（每次开发都按这个走）

### 1. 会话建立（必须最先做）

```
1. Connect                          —— 连接正在运行的 TIA Portal，或启动新实例
     └─ 失败原因：未装 TIA / 用户不在 "Siemens TIA Openness" 组
2. OpenProject  (或 CreateProject / AttachToOpenProject)
3. GetProjectTree                    —— 获得全部 devicePath / softwarePath / HMI 路径
```

> **务必在第一个操作后回读 GetProjectTree 的返回值。** 工具描述反复强调
> `softwarePath`（如 `PLC_1`）、`devicePath`、`blockPath`（如 `Program blocks/FBs/FB_Motor`）
> 必须来自树的真实输出。这是避免 "找不到路径 / 路径拼错" 的最大杠杆。

### 2. 变更循环（做任何修改后）

```
改动 → Compile / CompileAndDiagnosePlc（确认 0 错误） → SaveProject
```

- 需要结构化错误（带 Path + Description）用 **CompileAndDiagnosePlc**，简单成败用 **Compile**。
- 导出块之前必须确认 `IsConsistent=true`（否则先编译）。

### 3. 在线协作（涉及真机时才走）

```
GoOnline → CheckDownloadReadiness → DownloadToPlc → （完成后）GoOffline / TakeAllPlcOffline
```

> 在线写值 / 下载会**改变真实 PLC 行为**（下载时 CPU 短暂停机）。默认先
> CheckDownloadReadiness，并明确告知需要下载的意图与安全边界；不要把下载当成
> 编译后的自动下一步。

---

## 高频场景的推荐调用序列

> 路径参数（`softwarePath`、`devicePath` 等）**全部**来自前一步 GetProjectTree /
> GetSoftwareTree 的真实返回值，这里用占位符表示。

### A. 新建带 PLC（+可选 HMI）的项目

- 想一条命令搞定：用 **PlcBuildAndImport**（L1 一键生成器），先 `dryRun=true` 校验
  spec，再 `dryRun=false` 真正创建。
- 想逐步可控：
  1. `Connect` → `CreateProject`
  2. `SearchHardwareCatalog`（拿精确 MLFB）→ `AddDevice`（Siemens）/ `SearchInstalledGsdDevices` + 加 GSD（第三方）
  3. `ConnectDeviceNodesToProfinetSubnet`（把 PLC 与 HMI 接到 PROFINET 子网）
  4. `GetProjectTree` 校验硬件树

### B. 给 PLC 写程序块（UDT / 全局 DB / FC / FB）

- **首选（文本块）**：`PlcBuildAndImport`，`dryRun=true→false`，从结构化 JSON 直接建块并编译。
- 需要 SCL 源码：写本地 `.scl` 外部源（SCL 写文件工具），再 **ImportPlcExternalSource**。
- 需要 LAD：先写 S7DCL 文本（`.s7dcl` + `.s7res`），用 **ImportBlocksFromDocuments** 导入
  （离线 XML 构建器只产出 XML，供 ImportBlock 导入）。
- 组织块：用 MoveBlockToGroup 把块整理进分组（导出→删→重导，保留块号）。

### C. 读 PLC 实时值 / 追因"这个变量是谁写的"

| 目标 | 工具 |
|---|---|
| 快速读绝对地址实时值（只读） | **ReadPlcLiveValuesS7**（S7 协议 102 端口，`DB10.DBD0:REAL` 语法） |
| 读 OPC UA 节点值（只读） | ReadPlcLiveValuesOpcUa（需 CPU OPC UA 服务器已启用） |
| 趋势采样（看信号随时间变化） | 趋势采样工具（定间隔连续读，含 min/max/avg） |
| 静态追因（离线） | **TraceTagCause** —— 找所有写该标签的网络及门控条件 |
| 实时追因（在线） | **TraceTagCauseLive** —— 离线追因 + S7 实时读门控操作数 |
| 识别 CPU 身份（防连错机） | S7 读标识工具（回到模块型号 / 串号） |
| CPU 运行模式（RUN/STOP） | S7 读运行状态工具 |

> 绝对地址读 DB 前需 CPU 开启"允许 PUT/GET"，且 DB 为非优化访问（M/I/Q 无限制）。

### D. 报警 / OPC UA / 技术对象 / HMI

- **报警**：Import/Export 报警类别、报警文本列表（XLSX）、报警实例文本。
- **OPC UA**：读服务器配置 → 启用/禁用接口 → 导入/导出接口（XML）。
- **技术对象（轴/凸轮等）**：ListTechnologyObjects → ExportTechnologyObject / ImportTechnologyObject。
- **HMI（Classic/Unified）**：先 GetHmiSoftwareInfo 确认类型，再选 Classic / Unified 变体；
  Unified 用 Ensure 系列（画面 / 控件 / 标签 / 标签表）+ Apply 布局。

### E. 找不到对应专用工具时（通用后路）

用 **Reflection** 系列（L2）：

1. `Describe`（按 objectKind）列出对象成员 / 方法；
2. 按属性路径读值（read-only）；
3. 确实要写时，`Describe` 确认签名后再开启 `allowWrite`。

---

## 关于工具检索（配合 PLC Studio 的 tool_search）

PLC Studio 的工具注册器（`src/main/ai/tools/adapters/aiSdk/`）在工具集过大时会把
大部分工具**延迟暴露**，交给 `tool_search` / `tool_inspect` / `tool_invoke` 三个 meta 工具
按命名空间 + 查询词检索。因此：

- 当 TIA 工具没直接出现在工具列表里时，**先 `tool_search`**（用 `mcp:tia-mcp` 命名空间
  或功能关键词），再 `tool_inspect` 看签名，最后 `tool_invoke` 调用；
- 描述目标时带**领域词**（硬件 / 在线监控 / 报警 / HMI / 技术对象 / OPC UA / 反射 / 离线构建），
  这些词正是 tool 命名空间与 description 的检索入口。

---

## 需要时再说的内容

本指南聚焦"如何高效驱动"，不含：快捷任务模板（进阶，待需求明确后再开发）、
TIA MCP 服务器本体的 tool description 改写（该服务器是独立 .NET 二进制，不在本仓库）。

## 相关引用

- TIA MCP 接入实现：`src/main/data/db/seeding/seeders/tiaMcpSeeder.ts`
- 打包运行时：`resources/tia-mcp/v21/`
- 命令解析：`src/main/utils/bundledMcpCommand.ts`
- 工具注册与检索：`src/main/ai/tools/adapters/aiSdk/`（见 `docs/references/ai/tool-registry.md`）
