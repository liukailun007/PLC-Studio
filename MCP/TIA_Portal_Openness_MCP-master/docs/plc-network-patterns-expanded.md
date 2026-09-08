# PLC 程序扩展：网络与指令模式（提炼自工程实践 + SKILL 已验证子集）

本页在 `docs/basic-plc-template-library.md` 与 `tools/tiaportal-mcp/skill/SKILL.md`（LAD §9–11、SCL §10）基础上，补充 **常用网络结构** 与 **扩展指令方向**，便于在 `PlcBuildAndImport` / `ImportBlock` / 外部 SCL 中拉长「程序段」而不堆无意义重复。

## 1. LAD：建议的网络分段模式

| 网络段 | 典型内容 | 指令提示 |
|--------|-----------|-----------|
| 使能与联锁 | 急停、模式、许可 | 串接触点 → `Coil` / `SCoil` / `RCoil` |
| 模拟量/限幅 | 设定、上下限 | `Gt`/`Lt`/`Move`/`Add`/`Sub`/`Mul`/`Div` |
| 定时与沿 | 消抖、脉冲 | **TON/TOF/TP** 仅放在 **FB.Static** 或 **全局 DB**（见 SKILL：F-CPU 下勿放 FC.Temp） |
| 比较链 | 多段阈值 | `Eq`/`Ne`/`Ge`/`Le` 组合 + OR-box `O` |
| 数据类型转换 | 整型↔实型 | `Convert`（SrcType/DestType） |

**扩展建议**：在已有 `MCPVerify_FC_LAD*.xml` 基础上，按网络 **复制-改名-改操作数**，增加「报警锁存」「运行小时计数」「互锁矩阵」等独立网络，每网络单一职责。

## 2. SCL：`PlcBuildAndImport` DSL 与「超出 DSL」

- DSL 支持：`assignment`、`if`/`else`/`endif`、`line`（自由 token）、`elsif`（条件为 **单 Bool**）。  
- **不支持**：`FOR`/`WHILE`/`CASE` 等 → 用 **外部 `.scl` + ImportPlcExternalSource + Generate** 或 **TIA 内编写后 ExportBlock**。

**扩展建议**：将工艺拆为多个 **FC**：`FC_AlarmLatch`、`FC_ScaleReal`、`FC_Ramp`… 每个 30～80 行 SCL，再在 OB/Main 中顺序调用，比在单 FC 内堆巨型逻辑更易编译与下载。

## 3. 与参考工程（`reference`）的配合

打开 `reference\Siemens Standard Template V5_V21\*.ap21` 等，在 TIA 中搜索：

- `TON`、`CTU`、`MOVE`、`SEL`、`LIMIT`、`NORM_X`、`SCALE_X` 等块用法；  
- 多语言报警、工艺对象接口 DB 结构。

将 **接口 DB 成员命名** 与 `templates/plc/plcbuild-json/db_hmi_interface.json` 对齐，可减少 HMI 绑定时的符号歧义。

## 4. 蓝图内已有块与建议增量

| 已有（`templates/plc/plcbuild-json`） | 可增量方向 |
|----------------------------------------|------------|
| `fb_timer_counter_demo` | 增加 **CTU 级联**、预置值来自 HMI DB |
| `fb_step_sequence_demo` | 增加 **互锁步**、超时步、报警步 |
| `fc_math_compare_demo` | 增加 **Real 比较链** + 死区 `ABS` 模式（SCL 实现） |

具体 JSON 增量由项目工艺决定；本页只规定 **结构与指令选型** 原则，避免为自动化而生成不可维护的「千行单块」。
