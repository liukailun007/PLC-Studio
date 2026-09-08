# TIA ProjectContext — 快速上手 / 助手集成说明

对象：PLC Studio 里的“TIA 开发助手”（或任何要结合工程上下文的角色）。
作用：让助手在做修改前，先“扫一份当前工程的关系地图”再动手，避免 AI 对工程结构与变量瞎猜。

> 状态记录：2026-09-08 已把下方【项目上下文纪律】整段**直接写入**本机 PLC Studio
> 数据库里"TIA 开发助手"的系统提示词（agent id `aac5f5b4-e04b-46f9-b486-bea5e4fdf872`，
> `dist/data/Data/plcstudio.sqlite`）。启动 PLC Studio 加载该助手即生效，无需再手动粘。
> 若你想改文案，编辑 assistant 的提示词即可（本文件是持久化的母本）。

---

## 一句话
在 PLC Studio（仓库根目录）跑一条 Node 命令，即可把当前 TIA 工程只读扫描成一份
`out/project-context/maps.json`（设备、程序块、每个 FB/FC/DB 的接口变量及中文注释，
可选含 SCL 逻辑预览）。地图反映工程“当时”的状态。

## 命令（在仓库根目录执行）
```
cd D:\PLC-studio-main
node scripts/tia-project-scanner/scan.mjs --project "C:\Users\Administrator\Desktop\项目3\项目3.ap21" [--with-logic]
```
- `--project`：目标 `.ap21` 全路径。
- `--with-logic`：可选，额外把每个代码块的 SCL 逻辑预览前若干字符存进地图。
- 产物默认写到 `out/project-context/maps.json`；可用 `--out 其它路径` 改。
- 只读：它只把块导出到系统临时目录解析后自清，**绝不写回你的项目**。
- 前置：本机 TIA Portal V21 已装、用户在 `Siemens TIA Openness` 组（见主文档）。

## 地图长什么样（maps.json 字段语义）
- `devices[]`：设备（如 PLC_1=S7-1500、HMI_RT_1）
- `blocks[]`：每个程序块
  - kind/name/number/language/qualifiedBare（如 `FB1 "FB_MotorControl"`）
  - **members[]**：接口成员，每条含 `{name, dir, type, comment(中文注释)}`
    dir：INPUT / OUTPUT / IN_OUT / STATIC / TEMP
  - logicPreview（若 `--with-logic`）
- `hmi[]`：HMI 软件信息
- 由于 S7-1500 多为优化块(S7_Optimized)，一般不填手写绝对地址；要绝对地址需处理非优化 DB（未来增强）。

## 助手/提示词怎么“用上地图”（接法 C，手把手版）
给“TIA 开发助手”的系统提示里加下面这段，它就会在自己动手改块前会先看地图：

```
【项目上下文纪律】
在我读取或修改任何 TIA 工程前，若尚未确认工程结构，先执行：
  node scripts/tia-project-scanner/scan.mjs --project "<当前.ap21全路径>"
然后读取 out/project-context/maps.json。
依据地图回答“有哪些设备/程序块/哪些块引用了哪些变量及其含义”再动手；
改完一批块后，需要时可重跑一次让地图跟上，再据此核对影响面。
绝不在没有地图或未先 GetSoftwareTree 确认的情况下凭记忆猜块名/路径/变量。
```

（若你用的是会自己调 MCP 的智能体，就相当于“先摸地图再打枪”；对纯文本助手，则让人/文档先把 maps.json 内容贴给它。）

## 常见问题
- 地图不实时？ 对，它是快照；工程改了需重跑一次此命令。
- 变量中文看到乱码？ 本扫描器用 UTF-8 直读导出文件，已避开 MCP DescribeBlockLogic 那个乱码问题；
  请用能正确显示 UTF-8 的工具查看 maps.json。

## token 怎么省（很重要：别把整份 maps.json 塞进对话）
maps.json 是按块分隔的。给"TIA 开发助手"加一条**查询命令**，只回需要的那一小块，不整篇贴：

```
# 看概貌（~几百字节，很快、很省）——先要这个
node scripts/tia-project-scanner/query.mjs index
# 需要某一块的细节(含成员+中文注释)时，只拉那一个
node scripts/tia-project-scanner/query.mjs block FB_MotorControl
# 需要"某块按方向列出用到的变量"时
node scripts/tia-project-scanner/query.mjs uses FB_MotorControl
```

给助手提示词里加：
```
查询工程上下文时：
- 只调用上面 query.mjs 的 index / block <名> / uses <名>，绝不把整个 maps.json 内容复述进对话。
- 若本机已无 maps.json 或工程变化，先重跑 scan.mjs 再 query。
```

效果：索引只有几百字节；想看某一 FB 才拉那一块（通常几百到几千字节）。对话里长期只携带“小而准”的结果。

## 什么时候才“全自动重建 + 一键产品化”
本仓库脚本已是“命令即可重建 + 按块查询”闭环（用法如上）。要更“界面化/自动”（如在 PLC Studio 里做成一个按钮、或在改块后自动触发、指纹判断是否需重扫）需改 PLC Studio 自身代码（见 plan#9 之后的路线，较大、需重建 app），按需再投入。


