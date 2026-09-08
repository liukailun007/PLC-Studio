# PLC Studio — TIA ProjectContext V0.1 设计说明

> 状态：**V0.1 已完成样例扫描 —— 扫真实项目3生成 maps.json（2026-09-08）**
> 关键成果：新增只读扫描器 `scripts/tia-project-scanner/scan.mjs`，可用它对本机任意 TIA V21 项目扫描并产出干净的结构化关系地图。
> 目标读者：开发者本人（初级），此文档用尽量直白的话描述要做什么、为什么、怎么验收。

## 1. 我们要解决的问题（一句话）

当用户对 AI 说“把 3 号泵改成故障自动切 4 号泵”时，AI 必须先知道
**这个 TIA 项目里到底有什么、对应的 FB/DB、这些块互相怎么调用、改了会影响谁**，
而不是把整个工程一股脑塞给它、或者靠它瞎猜。

我们要做 **“项目上下文（ProjectContext）”**：扫描一次，整理成一张可查询的“关系地图”，
以后 AI 先看地图再动手。

## 2. 现实架构（已核实 && 已端到端跑通）

```
Cherry Studio (plc-studio, 本仓库)
  │ MCP 协议
  ▼
TIA Portal Openness MCP 服务器 (TiaMcpServer.exe)   ← C# 源码在仓库 MCP\ 下
  │ TIA Openness API（需本机 TIA V21 + 用户在 Siemens TIA Openness 组）
  ▼
TIA Portal V21
  = C:\Program Files\Siemens\Automation\Portal V21
  ▼
TIA 项目 = C:\Users\Administrator\Desktop\项目3\项目3.ap21
```

本次实测可用命令（无需 MCP host，直接引擎 CLI）：
- 自检：`...doctor --tia-portal-location "<Portal V21>" --tia-major-version 21`
- 只读开项目+看树+列块：`...describe "<项目3.ap21>" --plc PLC_1 --tia-portal-location "<Portal V21>" --tia-major-version 21 --logging 0`
  （exit 0 即为成功；`[stderr]` 里的 banner 是正常日志噪音，非报错。）

环境修复历史：用户起初不在 `Siemens TIA Openness` 组 → 加入后 **注销/重启生效**；现 doctor=READY。

## 3. 方案

用户确认信任开发者全权处理。

- **V0.1 先不加新 C#**，用引擎现有只读工具聚合出 JSON“关系地图”，由 Cherry 侧脚本/agent 消费。
- 后期如需产品化，再加 `BuildProjectIndex` 内聚工具（同一地图格式，平滑升级）。

## 4. 真实项目3 探查到的事实（2026-09-08 CLI describe 实测）

设备：
- `PLC_1` = S7-1500（导轨0 含 OPC UA_1、PROFINET 接口_1 [端口1/2]、读卡器）
- `HMI_RT_1` = WinCC 系列 HMI（PROFINET GT/端口1）

PLC_1 块（describe --plc 已列，首次枚举）：
- `OB Main`（Main，LAD）
- `FB FB_MotorControl`（SCL）电机控制
- `DB DB_MotorHMI`（GlobalDB，作为 HMI 接口数据）
- `FB FB_AI_Convert`（SCL）模拟量转换

→ 说明这是个小型的“电机/模拟量/HMI”示例工程，正好当 ProjectContext 开发夹具。
待枚举：完整块层级 / PLC 变量表 / HMI tag / 块间调用与交叉引用 / UDT。

## 5. V0.1 关系地图 JSON 结构（草案）

```
ProjectContext
├─ project : { name, version, path }
├─ devices : [{ name, family, detail }]
├─ blocks  : [{ name, number, kind:OB/FB/FC/DB/UDT, group, language }]
├─ edges   : [{ from, to, type:calls|instanceDb|uses }]
├─ tags    : [{ table, name, address, dataType, comment }]   (PLC & HMI)
└─ semantic: (阶段2) 人工标注：泵号/工艺/主备/规则…
```

验收（一个最小闭环）：
1. `describe`/枚举 → 产出 maps.json
2. 能回答“项目3 有哪些设备/块”
3. 能回答“FB_MotorControl 被谁调用、用了哪个 DB、对应 HMI”

## 6. 备份与安全

- GitHub：`main`(旧) + `backup-before-deepseek-project-context`(完整含MCP, 15e4d53)
- 项目3：只读打开/扫描，绝不写入。未经确认不删文件。

---

# 7. V0.1 完成日志（2026-09-08）

## 产出（本仓库新增）
- `scripts/tia-project-scanner/scan.mjs` —— 只读扫描器（MCP stdio→TIA Openness）
- `scripts/tia-project-scanner/probe.mjs` —— 只读探测脚本（调试/单工具调用用）
- `out/project-context/maps.json` —— 扫描项目3生成的 ProjectContext 关系地图
- 产出被写缓存为 git 不跟踪的文件（out/ 通常已 gitignore）

## 对真实项目3 扫出的结果
- 设备：`PLC_1`(S7-1500, OPC UA+PROFINET), `HMI_RT_1`(WinCC Unified)
- PLC_1 程序块（4）：
  - OB1 `Main` [LAD]
  - FB1 `FB_MotorControl` [SCL] —— 使用信号：StartCmd/StopCmd/FaultIn/LocalCmd/RemoteMode/FreqSetpoint/RunOut/FaultOut/FreqOut/state/faultAck…
  - GlobalDB1 `DB_MotorHMI`
  - FB2 `FB_AI_Convert` [SCL] —— 使用信号：AI_Raw/Scale_Low/Scale_High/HH|H|L|LL_Limit/Hyst/Raw_Min/Raw_Max/Value + HH/H/L/LL_Alarm…
- HMI_RT_1 Unified，屏幕：Main
- PLC tag 表：无独立表（该样例把信号放 FB/DB 接口内）

## 关键事实/边界（诚实记录）
1. **OB1 Main 当前并不调用 FB_MotorControl/FB_AI_Convert**：
   `GetCrossReferences(Main)=空`，且 `DescribeBlockLogic(Main) = “无 FlgNet / 不支持的语言”`。
   即示例工程里两个 FB 是独立作者单元，尚未接线进 OB1，因此本项目**暂时没有 OB→FB 调用边**。
   → “谁调用 FB_Pump”这类影响面 map 需在真的有调用的真实工程里做，或在后续阶段用 Block-Export+解析 CALL 补全。
2. **中文注释/中文 LAD 渲染文本在某几个工具里有乱码**（MCP 端 `LadTextRenderer` 等用系统默认编码读导出文件）。
   属于上游 MCP 的编码 bug，不进 V0.1 地图；V0.1 地图只保存干净的结构 + 每块使用的 operand 名。
3. 每块 cross-reference 已能干净给出“该块读写哪些 operands” —— 这是最有价值的工程上下文。

## 验收对照
- ✅ “项目3 有哪些设备/块”：有（devices + blocks）。
- ✅ “某 FB 用了哪些信号/对应哪些操作对象”：有（blockUses）。
- ↕ “被谁调用”（影响面 call-graph）：样例工程 OB1 不调用 FB，故暂无该边；真实带调用工程将能补上。

### 2026-09-08 更新：把 FB 接进 OB1 之后重新扫描，地图跟上了 + 依赖关系出现

用户把 FB_MotorControl/FB_AI_Convert 调进 OB1 后，重扫结果：
- 块从 4 → 6：自动生成了实例 DB `FB_AI_Convert_DB`(DB2)、`FB_MotorControl_DB`(DB3)。
- blockUses 出现依赖关系：`Main Uses` FB_AI_Convert/FB_MotorControl/两个_DB；且 FB_MotorControl 有 `UsedBy=Main` + `TypeInstance`（FB_AI_Convert 同理）。
- 结论：地图能反映工程变化，方式是重跑 scan.mjs（当前手动，非自动）。

自答用户三问（2026-09-08）：
1. 地图实时更新？→ 不自动，需重扫；自动化要“助手改完块→自动重建地图”（V0.2）。
2. 有变量表/能精到每个变量地址与含义？→ 样例的独立 PLC 变量表为空（信号在 FB/DB 接口内）；现能拿到块结构属性与跨引用 operand，但**拿不到每条 FB/DB 成员的绝对地址(%DBx.DBXy.z)与中文注释**——需导出块 SimaticML/.s7dcl 再解析 interface（属 V0.2 解析器，非免费）。
3. 接法 B(产品化)更顺？→ 更顺但工作量大（扫描需可一键触发+自动按需重建+存查）。

## 8. V0.2 候选（待确认范围）
A) 让地图在“工程改动后自动重建”（封装成 helper/命令，助手可调用）
B) 变量精读：导出块→解析接口→把每个成员 名/类型/偏移/初值/注释 收进地图
C) 接法 B 产品化：把“扫描/重建地图”做成助手可一键触发的工具 + 可查询
三块工作量递增。建议先从 A(自动重建)拿价值，B/C 按需再议。

---

# 9. V0.2 完成更新（2026-09-08）—— 接口变量解析 + 可复用扫描器

用户确认三件事(A/B/C)都做、按我判断推进。本轮完成一半。

## 新增/修改文件（本仓库，feat/project-context-scanner-v01 分支）
- `scripts/tia-project-scanner/parseS7dcl.mjs`  —— SIMATIC 导出文档解析库
  `parseS7dcl(dcl, s7res)` ：识别块 类型/名，抽 FB/FC/DB 接口变量 members[]
  （name, dir INPUT/OUTPUT/IN_OUT/STATIC/TEMP, type, mlcId, Chinese comment）
  `logicPreview(dcl)` ：取 SCL/NETWORK 逻辑前若干字符（UTF-8 直读，避开乱码）
- `scripts/tia-project-scanner/scan.mjs` —— 升级 v0.4：
  设备 + 块(编号/类型/语言/一致性/路径) + **逐块 members(含中文注释)** + HMI +
  可选 `--with-logic` 存逻辑预览。
  只读；块导出到系统临时目录解析后自清（`--export-dir` 可保留调测）。
- 生成物：`out/project-context/maps.json`（gitignore 不跟踪）。

## 真项目3 结果（v0.4 实测）
- 设备 PLC_1(S7-1500) + HMI_RT_1(Unified)
- blocks=6：Main(OB), FB_MotorControl(20成员), DB_MotorHMI(11成员), FB_AI_Convert(21成员),
  FB_AI_Convert_DB, FB_MotorControl_DB(实例DB，0成员)。
- 每个成员带中文注释，例如
  FB_MotorControl：StartCmd=启动命令, RunFeedback=运行反馈（接触器/变频器反馈）,
    RemoteMode=远程模式(1远程0本地), state=状态机0停1运2故障 …
  FB_AI_Convert：AI_Raw=模拟量原始值0~27648, HH/H/L/LL_Limit=高低高报警限值, Hyst=回差 …
- 逐块逻辑预览可用(--with-logic)。
- 中文注释通过读 .s7res 拿到；且 UTF-8 直读避免 DescribeBlockLogic 的中文乱码。

## 关键边界（诚实）
- 这些 S7-1500 块为 S7_Optimized（现代默认），通常无手填绝对地址 %DBx.DBXy.z；
  如需绝对地址需对 Standard-Layout / 非优化 DB 额外处理（该样例 DB 亦无手填偏移，仅类型+初值）。
- 每块“接口/成员地址级 + 注释”已由本版达成（地址仅在非优化时才有实体）。
- V0.2 的“自动化重建 + 助手一键触发(产品化 C)”仍待做。

## 复跑脚手架（人可一键，AI/助手可调）
```
cd D:\PLC-studio-main
node scripts/tia-project-scanner/scan.mjs --project "<项目>.ap21" [--with-logic]
# 产物：out/project-context/maps.json
```

