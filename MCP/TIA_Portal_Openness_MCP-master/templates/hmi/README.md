# WinCC Unified 画面模板库

本目录提供可直接交给 `ApplyUnifiedHmiScreenDesignJson` 的 WinCC Unified `designJson` 模板。模板采用通用工业界面结构：顶栏、导航、卡片、状态灯、参数输入、趋势占位、事件列表和诊断表。

## 视觉与参考素材

- 本目录 JSON 为 **脚本友好** 的扁平布局；若要接近西门子 **HMI Template Suite** 的观感，请在 TIA 中打开仓库 `reference` 下样板（见 `docs/optional-reference-materials.md`），把色值、间距、层次抄回 `designJson`。
- `unified_overview_1280x800.json` 已含 **顶栏强调色、侧栏色条、卡片阴影层** 等轻量美化；其它页可按同一手法叠「底层阴影 Rectangle + 上层白底卡片」。

## 模板一览

| 文件 | 建议尺寸 | 内容 |
|---|---:|---|
| `unified_overview_1280x800.json` | 1280 x 800 | 总览页：导航、命令、状态、过程区、事件摘要 |
| `unified_basic_dashboard_1024x768.json` | 1024 x 768 | Dashboard：状态、数值、Enable/Disable/Reset |
| `unified_control_strip_1024x768.json` | 1024 x 768 | 控制条：命令、状态灯、设定值 |
| `unified_parameter_page_1024x768.json` | 1024 x 768 | 参数页：设定值、上下限、时间、计数预置 |
| `unified_trend_page_1024x768.json` | 1024 x 768 | 趋势页：趋势区域、图例、实时数值 |
| `unified_basic_tag_diagnostics_1024x768.json` | 1024 x 768 | 标签诊断：Bool 指示、Real IOField |
| `hmi_tag_binding_snippets.json` | （人读）符号互连 / 绝对地址示例，配合 `docs/hmi-plc-tag-binding-and-addressing.md` |

1. `EnsureUnifiedHmiConnection`
2. `EnsureUnifiedHmiTagTable`
3. `EnsureUnifiedHmiTag`
4. `EnsureUnifiedHmiScreen`
5. `ApplyUnifiedHmiScreenDesignJson`
6. `BindUnifiedHmiTagDynamization`
7. `EnsureUnifiedHmiButtonAction`
8. 读回检查并保存

## 常用控件绑定

| 控件 | 建议绑定 |
|---|---|
| `Btn_Enable` / `Btn_Start` | `Down=set-bit`，`Up=reset-bit` |
| `Btn_Disable` / `Btn_Stop` | `Down=set-bit`，`Up=reset-bit` |
| `Btn_Reset` | `Down=set-bit`，`Up=reset-bit` |
| `Btn_Apply` | `Down=set-bit`，`Up=reset-bit` |
| `Lamp_Active` / `Lamp_Run` | Bool 状态 |
| `Lamp_Error` / `Lamp_Fault` | Bool 故障或错误状态 |
| `IO_Setpoint` / `IO_Actual` / `IO_Output` | Real 过程值 |
| `IO_OutputMin` / `IO_OutputMax` | Real 参数 |
| `IO_CounterPreset` | DInt 参数 |

## 画面规范

- 颜色使用 `0xAARRGGBB`。
- 顶层键为 `screen` 和 `items`。
- 控件类型优先使用 `Rectangle`、`Text`、`Button`、`IOField`。
- 页面尺寸必须与 `EnsureUnifiedHmiScreen` 的 width/height 一致。
- 绑定动作必须在画面控件创建后执行。
