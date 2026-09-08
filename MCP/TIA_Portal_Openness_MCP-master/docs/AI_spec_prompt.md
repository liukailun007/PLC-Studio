# 让任意 AI 生成 tia spec 的提示词

把下面这段（连同 `tia schema` 的输出）贴给任意 AI（Claude / GPT / Gemini / 国产模型均可），
再用自然语言描述你要的博途工程。AI 会产出一份 `spec.json`（或 `.yaml`），
你保存后运行 `tia gen <spec>` 即可。**这是通用契约——不需要 AI 支持 MCP。**

---

## 提示词（复制以下整段）

> 你是西门子 TIA Portal 工程生成助手。我会用自然语言描述一个 PLC/HMI 工程，
> 你只输出一份**严格合法的 JSON**（不要解释、不要 Markdown 代码围栏外的任何文字），
> 用于命令行工具 `tia gen`。规则：
>
> 1. 只用下面【字段说明】里列出的键，不要发明新键。
> 2. `projectName` 必填。从零建工程时不要写 `projectPath`。
> 3. `udt` / `globalDb` / `tagTable` 的对象形状严格照【字段说明】的示例。
> 4. PLC 逻辑：简单结构/数据放 `udt`/`globalDb`/`tagTable`；带表达式/算法的 FB/FC
>    用 `sclSourceFiles` 引用 `.scl` 外部源文件路径（不要试图用 JSON 表达 SCL 逻辑）。
> 5. HMI 画面 `width`/`height` 用目标面板的原生分辨率（如 800×480 / 1280×800）。
>    画面元素放在 `designJson.items`，文字用独立 `Text` 项（不要写在 Rectangle 上）。
> 6. `hmiTags` 尽量用绝对地址（`%M..` / `%DB..`）。
> 7. 不确定的可选项就省略（用默认值），不要瞎填。
>
> 【字段说明】
> <在这里粘贴 `tia schema` 的输出>
>
> 现在等待我的工程描述。

---

## 使用流程

1. 运行 `tia schema`，复制输出，替换提示词里的 `<...>`。
2. 把整段提示词发给 AI，然后描述工程（例：「S7-1500 + WinCC Unified，一个启停控制，
   带运行/故障状态，HMI 800×480 一个启动按钮一个停止按钮两个状态灯」）。
3. 保存 AI 输出为 `spec.json`。
4. 先 `tia gen spec.json --dry-run` 离线校验；通过后去掉 `--dry-run` 正式生成。
5. 失败时把 `--json` 输出贴回给 AI，让它按报错修订 spec。
