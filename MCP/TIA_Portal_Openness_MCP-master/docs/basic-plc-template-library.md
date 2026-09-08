# PLC 指令与模板说明

本说明用于配合 `templates/plc/` 生成通用 PLC 程序结构。模板覆盖 SCL、LAD 调用网络、变量表、UDT、全局 DB、FC、FB 和 HMI 接口 DB。

## 覆盖内容

| 类别 | 内容 |
|---|---|
| 布尔逻辑 | `AND`、`OR`、`NOT`、赋值、保持、复位 |
| 分支 | `IF / ELSIF / ELSE`、`CASE` |
| 定时 | `TON` 调用结构、延时完成位 |
| 计数 | 上升沿检测、累计、复位、到达预置 |
| 算术 | 加减乘除、误差、绝对值 |
| 比较 | `=`、`<>`、`>`、`>=`、`<`、`<=` |
| 限幅 | `LIMIT`、范围判断 |
| 类型转换 | `INT_TO_DINT`、`DINT_TO_REAL`、`REAL_TO_DINT`、`BOOL_TO_DINT` |
| 循环 | `FOR` |
| 数据结构 | Tag 表、UDT、Global DB、HMI 接口 DB |
| LAD | `BuildFlgNetCallXml` 调用网络配方和已验证 XML 样例 |

## 推荐导入顺序

```text
tagtable_basic_signals.json          (PlcBuildAndImport, tagtable)
udt_basic_status.json                (PlcBuildAndImport, udt)
db_basic_status.json                 (PlcBuildAndImport, globaldb)
db_hmi_interface.json                (PlcBuildAndImport, globaldb)
lad-recipes/lad_call_recipes.json    (BuildFlgNetCallXml)
scl-examples/FC_InstructionGallery.scl   (外部 SCL 导入)
scl-examples/FC_BasicScaleLimit.scl      (外部 SCL 导入)
scl-examples/FC_MathCompareDemo.scl      (外部 SCL 导入)
scl-examples/FB_BasicLatch.scl           (外部 SCL 导入)
scl-examples/FB_TimerCounterDemo.scl     (外部 SCL 导入)
scl-examples/FB_StepSequenceDemo.scl     (外部 SCL 导入)
```

> **FC/FB 一律走外部 SCL，不走 DSL。** `PlcBuildAndImport(kind=fc|fb)` 的 DSL 只接受
> 单变量名的 `condition`/`source`，不能解析 `Setpoint - Actual`、`Disable OR FaultLatch`、
> `ABS(...)`、`CASE` 等表达式（会编译报 `Tag not defined`）。因此含算术/比较/函数/CASE 的
> FC/FB 改为 `scl-examples/*.scl` 原生源，经 `ImportPlcExternalSource` +
> `GenerateBlocksFromExternalSource` 导入。旧 `plcbuild-json/fc_*.json`、`fb_*.json` 已弃用
> （文件保留并标 `_deprecated`，勿再使用其表达式写法）。

`plcbuild-json/*.json`（仅 tagtable/udt/globaldb）每个文件都包含：

```json
{
  "kind": "globaldb",
  "tool": "PlcBuildAndImport",
  "json": {}
}
```

调用时把 `json` 字段序列化为字符串，传给 `PlcBuildAndImport`。FC/FB 的 `.scl` 文件直接作为
外部源导入，不需要序列化。

## 验收

- 所有模板先 `dryRun=true`。
- 真实导入后执行 `CompileAndDiagnosePlc`。
- 编译错误为 0 后保存。
- HMI 变量应绑定到 PLC tag 或 `DB_HMI_Interface` 成员。
- LAD 调用网络用于组织调用关系，不手写复杂网络树。

## 扩展阅读

- **`docs/plc-network-patterns-expanded.md`**：多网络分段、定时器/沿放置规则、SCL 与 LAD 分工，用于把程序段从「演示级」拉长到「工程级」。
