# PLC 模板库

本目录提供 TIA Portal 项目生成所需的通用 PLC 模板，覆盖变量表、UDT、全局 DB、SCL FC/FB、LAD 调用配方和外部 SCL 源示例。模板不包含现场项目程序段。

## 目录

| 路径 | 用途 |
|---|---|
| `instruction-recipes/basic_plc_instruction_recipes.json` | 指令和语法配方 |
| `plcbuild-json/*.json` | `PlcBuildAndImport` 入参模板（仅 tagtable/udt/globaldb） |
| `lad-recipes/lad_call_recipes.json` | `BuildFlgNetCallXml` LAD 调用网络配方 |
| `scl-examples/*.scl` | 外部 SCL 源：FC/FB 功能块 + 指令示例 |

## PLC Build 模板（DSL：tagtable / udt / globaldb）

| 文件 | 类型 | 内容 |
|---|---|---|
| `tagtable_basic_signals.json` | tagtable | 命令、状态、过程值基础变量 |
| `udt_basic_status.json` | udt | 状态、数值、计数基础结构 |
| `db_basic_status.json` | globaldb | 基础状态 DB |
| `db_hmi_interface.json` | globaldb | HMI 命令、状态、参数、显示值接口 DB |

## FC/FB 功能块（外部 SCL，不走 DSL）

含算术/比较/函数/CASE 的 FC/FB 超出 `PlcBuildAndImport` 单变量 DSL 能力，统一用原生
`.scl` 经 `ImportPlcExternalSource` + `GenerateBlocksFromExternalSource` 导入。

| 文件 | 类型 | 内容 |
|---|---|---|
| `scl-examples/FC_BasicScaleLimit.scl` | fc | 线性缩放和 LIMIT 限幅 |
| `scl-examples/FC_MathCompareDemo.scl` | fc | ABS、比较、误差、限幅输出 |
| `scl-examples/FB_BasicLatch.scl` | fb | 布尔保持和复位 |
| `scl-examples/FB_TimerCounterDemo.scl` | fb | TON 定时、上升沿计数、复位 |
| `scl-examples/FB_StepSequenceDemo.scl` | fb | CASE 步序状态机 |
| `scl-examples/FC_InstructionGallery.scl` | fc | SCL 指令参考示例 |

> 旧 `plcbuild-json/fc_*.json`、`fb_*.json` 已弃用并标 `_deprecated`，请勿再用其表达式写法。

## 调用顺序

1. `Bootstrap`
2. `Connect`
3. `GetProjectTree`
4. `ValidateAutomationContext`
5. 逐个模板执行 `PlcBuildAndImport(dryRun=true)`
6. 按依赖顺序执行 `PlcBuildAndImport(dryRun=false)`
7. `CompileAndDiagnosePlc`
8. 读回块、变量表或编译诊断
9. `SaveProject`

推荐导入顺序：TagTable -> UDT -> GlobalDB -> 外部 SCL（FC/FB）-> LAD/XML 样例。

## 注意

- 所有 `softwarePath`、分组路径和设备路径必须来自 TIA 读回。
- `PlcBuildAndImport` 的 `json` 参数是字符串，调用前需要把模板中的 `json` 字段序列化。
- 外部 `.scl` 源导入前应保持 UTF-8 with BOM；若不确定，优先用 `PlcBuildAndImport` 生成 XML。
- 复杂 LAD/FBD 网络应使用 TIA 导出的已验证 XML 或 `BuildFlgNetCallXml` 生成调用网络。
