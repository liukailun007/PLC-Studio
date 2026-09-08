# 完整项目生成运行手册

本手册描述从空项目生成 PLC + WinCC Unified 工程的完整流程。所有文件均在交付包内。

## 零、交付包自检（推荐先做）

在包根目录执行（无需启动 TIA）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Validate-Bundle.ps1
```

通过后再连 MCP；`-Strict` 可选。

## 输入文件

| 文件 | 用途 |
|---|---|
| `tools/tiaportal-mcp/skill/SKILL.md` | 工具调用规则 |
| `templates/project-blueprints/full_plc_hmi_project.json` | 项目蓝图 |
| `templates/plc/README.md` | PLC 模板索引 |
| `templates/hmi/README.md` | HMI 模板索引 |
| `docs/basic-plc-template-library.md` | PLC 指令说明 |
| `docs/HMI_Unified_画面生成规范与模板.md` | HMI 画面规范 |
| `docs/hmi-plc-tag-binding-and-addressing.md` | HMI↔PLC 符号/绝对地址与红字排障 |
| `docs/mcp-ide-and-tool-visibility.md` | MCP 与 IDE 无关、工具列表权威来源 |
| `docs/optional-reference-materials.md` | 与仓库 `reference` 样板配合 |
| `docs/plc-network-patterns-expanded.md` | PLC 网络与指令扩展写法 |

## 一、环境检查

```text
Bootstrap
Connect
GetState
```

检查项：

- TIA Portal 可连接。
- 用户具备 Openness 权限。
- PublicAPI 与 TIA 版本匹配。
- 当前会话没有未处理的错误。

## 二、创建项目与硬件

```text
CreateProject
AddDeviceWithFallback
AddHardwareCatalogDeviceWithProbe
ConnectDeviceNodesToProfinetSubnet
GetProjectTree
ValidateAutomationContext
```

要求：

- CPU 与 HMI 实例创建成功。
- PROFINET 连接有读回证据。
- PLC software path、HMI software path、PLC name 均来自 `GetProjectTree`。

## 三、生成 PLC

导入顺序：

```text
tagtable
udt
globaldb
fc
fb
ladRecipe
externalSclExample
compile
```

模板来源：

```text
templates/plc/plcbuild-json/*.json
templates/plc/lad-recipes/lad_call_recipes.json
templates/plc/scl-examples/FC_InstructionGallery.scl
```

执行要求：

1. 每个 `plcbuild-json` 模板先执行 `PlcBuildAndImport(dryRun=true)`。
2. dryRun 通过后再执行 `dryRun=false`。
3. 真实导入后执行 `CompileAndDiagnosePlc`。
4. 编译错误为 0 后进入 HMI 生成。

## 四、生成 HMI

画面：

```text
Overview
Dashboard
ControlStrip
Parameters
Trend
TagDiagnostics
Events
```

执行顺序：

```text
GetHmiProgramInfo
EnsureUnifiedHmiConnection
EnsureUnifiedHmiTagTable
EnsureUnifiedHmiTag
EnsureUnifiedHmiScreen
ApplyUnifiedHmiScreenDesignJson
BindUnifiedHmiTagDynamization
EnsureUnifiedHmiButtonAction
```

要求：

- HMI 连接必须使用 `GetProjectTree` 读回的实际 PLC 软件节点，不手写目录显示名；工具会按 PLC 设备 `TypeIdentifier` 推断 S7-1200/1500/300/400 驱动，并写入 Partner、Station、Node。
- HMI Tag 按蓝图 `tags[]` 同时传入 `plcTag` 与 `address`：`plcTag` 用于符号说明和读回诊断，`address` 绑定到 `DB_HMI_Interface` 的标准访问绝对地址（例如 `%DB200.DBX0.0`）。
- `DB_HMI_Interface` 必须先导入并编译，且保持 `MemoryLayout=Standard`、`dbNumber=200`，否则 HMI 变量无法稳定连到 PLC 内部数据。
- 画面尺寸与模板一致。
- 按钮动作使用 `Down` / `Up`。
- 动态化绑定在控件创建后执行。

## 五、验收

必须满足：

- `GetProjectTree` 可读回 PLC 和 HMI。
- PLC 编译错误为 0。
- HMI 画面创建成功。
- HMI 连接读回 `CommunicationDriver` 与实际 PLC 系列匹配，`Partner/Station/Node` 至少有一个可解释的实际 PLC/PN 接口值。
- HMI Tag 读回 `Connection=HMI_Connection_1`，并且 `Address` 或 `LogicalAddress` 等于蓝图中的 `%DB200...` 地址。
- `ApplyUnifiedHmiScreenDesignJson` 无不可解释失败。
- 按钮动作通过 SyntaxCheck。
- 动态化绑定返回成功或可读回。
- `SaveProject` 成功。

## 六、失败处理

| 现象 | 处理 |
|---|---|
| 找不到 software path | 重新执行 `GetProjectTree`，不要猜路径 |
| PLC 导入失败 | 回到 dryRun 输出，检查生成 XML 和导入类型 |
| HMI 控件找不到 | 先应用画面模板，再绑定动作和动态化 |
| HMI Tag 红字 | 检查连接、PLC 符号、DB 成员和编译状态 |
| 编译错误 | 导出诊断，修正 PLC 模板或导入顺序 |
