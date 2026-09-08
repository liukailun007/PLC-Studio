# TIA ProjectContext 工具箱（只读）

把任意 TIA V21 工程扫描成一份"项目上下文" maps，并能**按块小块查询**，
让 AI/助手动手前先"看着工程地图"而不是瞎猜，同时省 token（不整篇贴）。

## 前置
- Node 18+/pnpm（仓库根目录 `node_modules` 已含 MCP SDK）。
- 本机已装 TIA Portal V21 + 用户在 `Siemens TIA Openness` 组（注销重登生效）。
- 一律在仓库根目录执行（用相对 `node_modules/@modelcontextprotocol/sdk`）。

## 三件工具（都要在仓库根目录跑）
1. 生成/刷新地图（只读）
   ```
   node scripts/tia-project-scanner/scan.mjs --project "C:\...\项目3\项目3.ap21" [--with-logic]
   # 产物: out/project-context/maps.json
   ```
2. 看概貌 —— 很小（几百字节），先要它
   ```
   node scripts/tia-project-scanner/query.mjs index
   ```
3. 按块查细节 —— 只取你要的那一块
   ```
   node scripts/tia-project-scanner/query.mjs block FB_MotorControl
   node scripts/tia-project-scanner/query.mjs uses  FB_MotorControl
   ```

## maps.json 里有什么
- `project`, `devices[]`, `hmi[]`
- `blocks[]` 每条含 kind/name/number/language/qualified + `members[]`
  （FB/FC/DB 的接口成员：`{name, dir:INPUT|OUTPUT|IN_OUT|STATIC|TEMP, type, comment(中文)}`）
- 加 `--with-logic` 会带每块 SCL 逻辑预览(UTF-8 直读，避开 MCP 乱码)。

## 给"TIA 开发助手"的一段提示词（直接可用）
```
【项目上下文纪律】
动手读/改 TIA 工程前，先确认工程结构：
- 概览：node scripts/tia-project-scanner/query.mjs index
- 需要某一 FB/DB 变量细节：(仓库根目录) node .../query.mjs block <块名>
- 查某块按方向用到的变量：…… query.mjs uses <块名>
只在 maps.json 缺失/工程变化时才重跑 scan.mjs。
绝不在没有上面任一依据时凭记忆编块名/路径/变量；也不要整篇复述 maps.json（省 token）。
```

## 为什么这样省 token
`query.mjs index` 约几百字节；`block <名>` 只回一块。
AI 只会携带它真正需要的极小片段，而不是整份 map。
