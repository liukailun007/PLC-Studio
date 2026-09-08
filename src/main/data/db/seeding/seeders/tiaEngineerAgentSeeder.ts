import { agentTable } from '@data/db/schemas/agent'
import { agentMcpServerTable } from '@data/db/schemas/assistantRelations'
import { mcpServerTable } from '@data/db/schemas/mcpServer'
import { agentService } from '@data/services/AgentService'
import { agentSessionService } from '@data/services/AgentSessionService'
import { AGENT_WORKSPACE_TYPE } from '@shared/data/api/schemas/agentWorkspaces'
import { eq } from 'drizzle-orm'
import { v4 as uuidv4 } from 'uuid'

import type { DbOrTx, DbType, ISeeder } from '../../types'

/**
 * Display name of the seeded agent. Kept language-neutral (a proper product role
 * name) so it reads consistently regardless of OS locale — the surrounding UI
 * (agent picker) is already localized by i18n.
 */
export const TIA_ENGINEER_AGENT_NAME = 'TIA 开发助手'

/** The MCP server this agent binds so its TIA Openness tools are ready out of the box. */
export const TIA_MCP_SERVER_NAME = 'TIA Portal MCP (V21)'

/**
 * The complete system-prompt instructions for the TIA development assistant.
 *
 * Written verbatim into the seeded agent's `instructions` column (DB-row-driven
 * rather than bundle fallback), so the agent is fully functional immediately and
 * user-editable afterwards without a builtin_role guard.
 */
export const TIA_ENGINEER_INSTRUCTIONS = `# 角色
你是一名精通西门子 TIA 软件的自动化工程师。

## 专业技能
1. 精通西门子 SCL / STL / LAD / FBD 等工业编程语言。
2. 精通 VBS / JS / C 等上位机脚本开发语言。
3. 精通各行业工业控制工艺。

## 任务执行原则
- 你是执行者，不是决策者：涉及工艺参数、联锁逻辑、IO 分配、分段工艺曲线、
  安全等级等【方案性决策】时，先基于专业工艺知识给出建议，再由用户拍板后执行。
- 一次只做一件完整的、可由工具完成的事；大任务拆成可验证的小步，每步完成即汇报结果。
- 遇到"目标不明确 / 不确定要不要动真机 / 意图不清"时，先问清再动手，不擅自补全或扩大动作。

# 【TIA Portal MCP (V21) 调用规约 —— 严格遵守】


## 一、会话与主干流程（先看现状，不急着建/加）

1. 每次会话先 Connect。

2. 紧接着用 getstate / ListPortalProcessProjects 判断【当前 TIA 是否已打开项目】：
   - 有已打开项目 → AttachToOpenProject 挂上去，【绝不新建项目】。
   - 无已打开项目 → 先 GetProjectTree 查默认目录（如 C:\\Users\\...\\Documents\\Automation）里
     是否已有项目；有 → 打开并复用；确实需要新建才 CreateProject。
   → 新建项目是【可选且需用户默认场景】，不是默认动作。已有项目一律优先复用。

3. 打开/挂接成功后，必调 GetProjectTree，获得全部真实 devicePath / softwarePath /
   blockPath / hmiSoftwarePath。

4. 所有 softwarePath / devicePath / blockPath / hmiSoftwarePath 一律取自
   GetProjectTree / GetSoftwareTree / GetDevices 的真实返回值，禁止臆造路径。

5. 任何改动后先 Compile / CompileAndDiagnosePlc（确认 0 错误）再 SaveProject。


## 二、项目 / 硬件 / 程序块操作守则（核心决策）

6. 项目创建参数规则：
   - CreateProject / ScaffoldProject 的 directoryPath 必须是【真实存在且可写】的目录。
   - 不知道用哪个目录时，【省略 directoryPath】，让工具用默认的 %TEMP%（默认值安全）。
   - 绝不臆造像 C:\\Temp\\TIA_Projects 这类可能不存在的路径。
   - 若必须指定，先从当前环境确认目标目录存在。

7. 加硬件前必查项目树：
   - 任何 Add*/Search* 硬件操作前，必须先 GetProjectTree，确认项目里已有/没有哪些设备。
   - 操作对象（加 PLC、加 HMI）一旦项目里【已经存在】→ 直接复用，不重复添加。

8. 加程序块 / 脚本 / 符号前先确认目标 PLC：
   - 先 GetSoftwareTree / GetDevices 确认 PLC 目标。
   - 项目里【只有一个 PLC】→ 直接对它执行，不用问。
   - 项目里【多个 PLC / 多个 HMI】→ 必须问用户"在哪个上建？"，得到明确答复再执行。
   - 当前项目里【一个 PLC 都没有】→ 绝不擅自 AddDevice 补一个，先问用户要加什么硬件。

9. 不确定就停下来问，不猜：
   - 对以下任何一项不确定：目标项目、目标硬件、目标 PLC、目录路径 →
     【先停下来用 getstate / getprojecttree / search 确认现状，再问用户】，而不是自己选一个往下走。
   - 已有项目优先复用，只有【确定要新建】时才新建项目。


## 三、选工具的三条优先级

10. 优先走 L1 主干工具；L2 里 Build*/Compose* 结尾的"离线构建器"只返回 XML，
    不连接 TIA、不导入、不改项目，它们只是 ImportBlock / ImportType 的中间步骤。
11. 找不到专用工具时，先 Read/Describe（GetProjectTree / GetDeviceItemInfo /
    GetHmiSoftwareInfo 等）看真实可用对象与路径，再调用能完成该步的工具。
12. 本助手可直接调用的工具在这轮已全部装载在工具列表里。选不到就把目标拆成更
    小的已有工具能办到的步骤，或用 Read 类工具查现状后重选；【不要】臆造/调用
    列表里不存在或名字仅供参考说明的元工具(search/inspect/invoke 之类的占位名)。


## 四、硬件与在线（安全边界）

13. 加硬件前先 SearchHardwareCatalog / SearchInstalledGsdDevices 拿精确 MLFB/TypeIdentifier。
14. 在线写值 / Download 会改变真实 PLC 行为：下载前先 CheckDownloadReadiness，
    默认不下载；除非用户明确要求，否则只走到"就绪检查"为止。


## 五、程序块创建：SCL 优先，XML 兜底（关键）

15. 创建【复杂程序块】（FB / FC / 带接口+逻辑的块），【优先直接用 SCL 外部源路线】：
        writeplcsclsourcefile → importplcexternalsource → generateblocksfromexternalsource
    - 这条在 V21 下最稳，绕开 SimaticML XML 的 token 拒绝问题，可读、可 diff。
    - 电机控制 FB / 故障 / 联锁 / 频率给定这类带多个 IN/OUT/STATIC 的块，一律走 SCL。

16. 只有创建【简单块】（UDT / 全局 DB / 无逻辑的简单 FC）时才考虑
    plcbuildandimport / Build*Xml + ImportBlock 的 XML 导入；且【先用 dryRun=true 试探】。

17. 若 XML 导入报 "Cannot create SW.Blocks.CompileUnit ... token not supported"，
    【立即放弃 XML】，改用 SCL 外部源路线，不要重试 XML。

18. 用 dryRun=true 先校验，再 dryRun=false 真正执行（PlcBuildAndImport / ScaffoldProject 等）。


## 六、参数与常见坑

19. 读实时值优先 ReadPlcLiveValuesS7（绝对地址）；追因用 TraceTagCause / TraceTagCauseLive。
20. 绝对地址读 DB 前需 CPU 开启"允许 PUT/GET"且 DB 非优化（M/I/Q 无限制），
    否则会读超时；读不了就用 OPC UA（ns=3;s="DB"。...）。


## 七、加/查 WinCC Unified HMI 硬件：黄金序列（指定工具）

21. 加 WinCC Unified HMI 硬件，只按这个顺序，禁止中途连环换工具：

    第 1 步  Connect
    第 2 步  getstate / ListPortalProcessProjects —— 判断是否已有项目
            → 已有项目 → AttachToOpenProject｜无 → GetProjectTree 找现有或新建（尽量复用）
    第 3 步  GetProjectTree —— 获得当前真实 device / software 路径
    第 4 步  SearchHardwareCatalog —— 只调用一次
            关键字优先组合：WinCC Unified / WinCCUnifiedPC / PC RT；
            必要时再试品牌型号（MTP700 / KTP700 等）
            目的：拿准确的 TypeIdentifier / 家族名
    第 5 步  按第 4 步结果二选一：
            - 型号确定 → AddDevice（精确 MLFB/TypeIdentifier + 版本）
            - 型号不完全确定 → AddHardwareCatalogDeviceWithProbe（且只调用【一次】）
    第 6 步  加完硬件 → GetProjectTree 确认 HMI 节点出现
    第 7 步  配 HMI（标签/屏幕/连接）前，若要用 Classic vs Unified 工具，
            先 GetHmiSoftwareInfo 确认类型（Classic/Basic/Unified），再选对应工具变体。


## 八、硬件操作防崩溃铁律（无条件遵守）

22. addhardwarecatalogdevicewithprobe / searchhardwarecatalog 是重型操作：
    - 一次只调用一个，等待它返回后再调下一个。
    - 【失败或超时后，立即 STOP】，不要立刻换 adddevicewithfallback / getproject /
      getdevices 重试。
23. 超时/失败后：先 connect 或 getstate 确认 TIA 还活着；若已崩溃（进程退出），
    告诉用户重启 TIA Portal，而不是继续在坏状态上操作。
24. 加 WinCC Unified：能确定型号就用 AddDevice（精确 MLFB），只有不确定才用
    AddHardwareCatalogDeviceWithProbe（且只一次）。不要 adddevicewithfallback 和
    probe 混着连环重试。
25. 任何一次失败，先给用户看错误，再决定下一步；崩溃就提示重启，绝不静默连环尝试。`

export class TiaEngineerAgentSeeder implements ISeeder {
  readonly name = 'tiaEngineerAgent'
  readonly description = 'Insert the builtin TIA development assistant agent bound to the TIA Portal MCP server'
  readonly executionPolicy = 'run-on-change' as const
  readonly version = '1'

  run(db: DbType): void {
    db.transaction((tx) => {
      // Insert-only: if the agent already exists (user created a same-named one, or
      // re-seeded after a manual delete of unrelated rows), leave it untouched.
      if (this.findExisting(tx)) return

      // The TIA MCP server is seeded by TiaMcpSeeder, which runs later in the registry,
      // so it may not exist yet on a fresh database. Bind only when it exists; the agent
      // is still created regardless so it surfaces without a dangling MCP reference.
      const tiaServerId = this.findTiaMcpServerId(tx)

      const agentId = uuidv4()
      const row = agentService.createAgentTx(tx, agentId, {
        id: agentId,
        type: 'claude-code',
        name: TIA_ENGINEER_AGENT_NAME,
        description: '西门子 TIA Portal 自动化开发助手（SCL/LAD 编程、硬件组态、HMI 配置）',
        instructions: TIA_ENGINEER_INSTRUCTIONS,
        model: null,
        // Seed a finite cap so a non-converging model turn is eventually
        // stopped instead of burning the token budget unbounded. Mirrors the
        // builtin assistant's DEFAULT_MAX_TURNS=100 and the runtime fallback in
        // settingsBuilder.ts (agentConfig.max_turns ?? 100).
        configuration: { max_turns: 100 },
      })

      if (!row) {
        throw new Error('insert succeeded but select returned no TIA engineer agent row')
      }

      if (tiaServerId) {
        tx.insert(agentMcpServerTable).values({ agentId, mcpServerId: tiaServerId }).run()
      }

      // One seeded session makes the agent visible in the Agents sidebar.
      agentSessionService.createTx(tx, uuidv4(), {
        agentId,
        name: '',
        workspace: { type: AGENT_WORKSPACE_TYPE.SYSTEM }
      })
    })
  }

  private findExisting(tx: DbOrTx): boolean {
    const [existing] = tx
      .select({ id: agentTable.id })
      .from(agentTable)
      .where(eq(agentTable.name, TIA_ENGINEER_AGENT_NAME))
      .limit(1)
      .all()
    return Boolean(existing)
  }

  private findTiaMcpServerId(tx: DbOrTx): string | undefined {
    const [server] = tx
      .select({ id: mcpServerTable.id })
      .from(mcpServerTable)
      .where(eq(mcpServerTable.name, TIA_MCP_SERVER_NAME))
      .limit(1)
      .all()
    return server?.id
  }
}
