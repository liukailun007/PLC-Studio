# LAD 指令库（中性参考）

本文件汇总在 LAD（梯形图）网络中常见的指令、引脚、操作数与连接方式，配合 `tools/tiaportal-mcp/skill/SKILL.md` 中已验证的 `Part Name` 注册表使用。所有示例均为通用语法，不绑定特定工艺。

## 1. 网络划分原则

一个 CompileUnit 通常对应一个网络。建议按职责拆段：

| 段类型 | 用途 |
|--------|------|
| 使能与联锁 | 急停、模式、互锁条件 |
| 输入预处理 | 模拟量缩放、滤波 |
| 命令处理 | 启停命令、置位/复位 |
| 工艺主逻辑 | 步序、定时、计数 |
| 输出映射 | 命令输出、报警输出 |
| 诊断 | 状态字、错误码 |

## 2. 触点与线圈

| 名称 | Part Name | 引脚 |
|------|-----------|------|
| 常开触点 | `Contact` | `in`, `out`, `operand` |
| 常闭触点 | `Contact` + `<Negated Name="operand"/>` | 同上 |
| 输出线圈 | `Coil` | `in`, `operand` |
| 置位线圈 | `SCoil` | `in`, `operand` |
| 复位线圈 | `RCoil` | `in`, `operand` |
| 并联（OR） | `O` （TemplateValue `Card=2`） | `in1`/`in2`/.../`out` |

## 3. 沿检测

| 名称 | Part Name | 备注 |
|------|-----------|------|
| 上升沿 | `PBox` | 需要同操作数两份 `IdentCon`（两个 `Access`/`UId`） |
| 下降沿 | `NBox` | 同上 |

## 4. 比较

| 名称 | Part Name | 模板值 | 引脚 |
|------|-----------|--------|------|
| 等于 | `Eq` | `SrcType=Int/DInt/Real/...` | `pre`, `in1`, `in2`, `out` |
| 不等于 | `Ne` | 同上 | 同上 |
| 大于 | `Gt` | 同上 | 同上 |
| 大于等于 | `Ge` | 同上 | 同上 |
| 小于 | `Lt` | 同上 | 同上 |
| 小于等于 | `Le` | 同上 | 同上 |

## 5. 算术

| 名称 | Part Name | 模板值 | 引脚 |
|------|-----------|--------|------|
| 加 | `Add` | `SrcType`+`Card` | `en`, `eno`, `in1`, `in2`, `out` |
| 减 | `Sub` | 同上（建议加 `DisabledENO="true"`） | 同上 |
| 乘 | `Mul` | `SrcType`+`Card=2` | 同上 |
| 除 | `Div` | 同上 | 同上 |
| 取模 | `Mod` | `SrcType=Int/DInt` | 同上 |
| 绝对值 | `Abs` | `SrcType=Int/DInt/Real` | `en`, `eno`, `in`, `out` |
| 取反 | `Neg` | 同上 | 同上 |

## 6. 数据搬运与转换

| 名称 | Part Name | 备注 |
|------|-----------|------|
| 传送 | `Move` | TemplateValue `Card=1` |
| 数据类型转换 | `Convert` | TemplateValue `SrcType`, `DestType` |
| 串并行 | `Serialize` / `Deserialize` | 字节级 |
| 散收 | `SCATTER` / `GATHER` | 位/字段 |

## 7. 定时器（IEC）

| 名称 | Part Name |
|------|-----------|
| 通电延时 | `TON` |
| 断电延时 | `TOF` |
| 单脉冲 | `TP` |

定时器需要实例：

- 推荐声明在 **FB Static 段**，类型用 `TON_TIME` / `TOF_TIME` / `TP_TIME`；
- 也可使用 **独立全局 DB**，在 LAD 中通过 `<Instance Scope="GlobalVariable">` 引用；
- F-CPU 标准侧 FC 的 Temp 区 **不允许** 直接放 IEC 定时器实例。

## 8. 计数器（IEC）

| 名称 | Part Name |
|------|-----------|
| 增计数 | `CTU` |
| 减计数 | `CTD` |
| 增减 | `CTUD` |

## 9. 表达式块 Calc

`Calc` 用于在一个块内写多操作数的混合表达式：

- 模板值：`Card`（操作数数量）、`SrcType`、`<Equation>...</Equation>`；
- 在网络密集时比 `Add/Sub/Mul/Div` 串接更紧凑。

## 10. XML 注意事项

参考 `tools/tiaportal-mcp/skill/SKILL.md` §9–§11：

- `<FlgNet>` 内 `UId` 必须为十进制；
- 删除所有 `<!-- -->` XML 注释；
- `&`、`<`、`>` 在 `<Text>` / 注释里需转义；
- `<ProgrammingLanguage>` 同时存在于 **块层** 与 **每个 CompileUnit**；
- 导入失败时优先看 `Portal.cs::UnwrapImportError` 给出的异常链尾段。

## 11. 模板与示例

| 文件 | 内容 |
|------|------|
| `tools/tiaportal-mcp/skill/lad-cookbook/MCPVerify_FC_LAD.xml` | 基础触点/线圈/比较/搬运 |
| `tools/tiaportal-mcp/skill/lad-cookbook/MCPVerify_FC_LAD_v2.xml` | 算术、转换、取反触点 |
| `tools/tiaportal-mcp/skill/lad-cookbook/MCPVerify_FC_LAD_v3.xml` | FC 中的比较网络 |
| `tools/tiaportal-mcp/skill/lad-cookbook/MCPVerify_FB_LAD_v3.xml` | FB 中的 IEC 定时器实例 |
