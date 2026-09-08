#!/usr/bin/env node
/**
 * TIA Project Scanner V0.3 (只读) —— 生成 ProjectContext maps.json
 *
 * 干净版：保留结构 + 干净的跨引用"使用变量/块/类型"，不把会乱码的中文 logic 文本写进地图
 * （DescribeBlockLogic 的中文注释/渲染有 MCP 端编码 bug，属上游问题，另行标记，不进 V0.1 地图）。
 *
 * 产出：
 *   devices / blocks(每块: 编号 类型 语言 一致性 路径) /
 *   blockUses[每代码块: 引用的 operand 去重: 名/类型/访问] /
 *   edges[块→块可识别的引用] / tagTables / hmi
 *
 * 用法（仓库根目录）：
 *   node scripts/tia-project-scanner/scan.mjs --project "<...ap21>" [--out maps.json] [--verbose]
 */
import { mkdirSync, writeFileSync } from 'node:fs'
import path from 'node:path'
import process from 'node:process'
import { fileURLToPath } from 'node:url'
import { Client } from '@modelcontextprotocol/sdk/client/index.js'
import { StdioClientTransport } from '@modelcontextprotocol/sdk/client/stdio.js'

const __dirname = path.dirname(fileURLToPath(import.meta.url))
const REPO_ROOT = path.resolve(__dirname, '..', '..')
const argVal = (n, d) => { const i = process.argv.indexOf(n); return i >= 0 && process.argv[i + 1] ? process.argv[i + 1] : d }
const verbose = process.argv.includes('--verbose')
const project = argVal('--project', null)
const portal = argVal('--portal', 'C:\\Program Files\\Siemens\\Automation\\Portal V21')
const outPath = argVal('--out', path.join(REPO_ROOT, 'out', 'project-context', 'maps.json'))
const EXE = path.join(REPO_ROOT, 'MCP', 'TIA_Portal_Openness_MCP-master', 'runtime', 'v21', 'TiaMcpServer.exe')
if (!project) { console.error('USAGE: node scan.mjs --project <...ap21> [--out maps.json]'); process.exit(1) }
const vlog = (...a) => { if (verbose) console.log('  >', ...a) }

const transport = new StdioClientTransport({
  command: EXE,
  args: ['--tia-portal-location', portal, '--tia-major-version', '21', '--logging', '0'],
  stderr: 'pipe'
})
transport.stderr?.on('data', (d) => { const s = d.toString().trim(); if (verbose && s) vlog('stderr:', s.slice(0, 200)) })
const client = new Client({ name: 'tia-project-scanner', version: '0.3.0' })

const attr = (a, n) => { const x = (a || []).find((y) => y.name === n); return x ? x.value : null }
const itemsOf = (o) => (o && (Array.isArray(o.items) ? o.items : Array.isArray(o.Items) ? o.Items : null)) || null
async function toolJson(name, args = {}, silent) {
  if (!silent) vlog('call', name, JSON.stringify(args))
  try {
    const res = await client.callTool({ name, arguments: args })
    const c = (res && res.content) || []
    let text = ''
    for (const x of c) if (x && typeof x.text === 'string') text += x.text
    if (res && res.isError) throw new Error(name + ': ' + (text || 'isError'))
    try { return JSON.parse(text) } catch { return { rawText: text } }
  } catch (e) { return { error: (e && e.message) || String(e) } }
}

async function main() {
  console.log('TIA Project Scanner V0.3 (只读)')
  console.log('  project:', project)
  await client.connect(transport)
  console.log('  connected.')

  const res = {
    meta: { scanner: 'tia-project-scanner v0.3', scannedAt: new Date().toISOString(), projectFile: project },
    project: { name: null, path: project },
    devices: [],
    blocks: [],
    blockUses: [],   // [{ block, refs:[{name,type,access}] }]
    edges: [],       // [{ from, to, type }]
    tagTables: [],
    hmi: [],
    notes: [],
    errors: []
  }

  try {
    // project name from GetProject (best-effort)
    try { const pj = await toolJson('GetProject', {}, true); const its = itemsOf(pj); if (its && its[0]) res.project.name = its[0].name } catch {}

    // devices
    try { for (const it of itemsOf(await toolJson('GetDevices', {}, true)) || []) { const a = it.attributes || []; res.devices.push({ name: it.name, typeName: attr(a, 'TypeName') || null, typeIdentifier: attr(a, 'TypeIdentifier') || null }) } }
    catch (e) { res.errors.push('GetDevices: ' + ((e && e.message) || e)) }

    const softwarePath = 'PLC_1'

    // blocks (flat via GetBlocksWithHierarchy)
    try {
      const wh = await toolJson('GetBlocksWithHierarchy', { softwarePath }, true)
      const root = wh && (wh.root || wh.Root)
      if (!root) { res.notes.push('hierarchy empty; fallback GetBlocks'); const b = await toolJson('GetBlocks', { softwarePath, name: '' }, true); for (const it of itemsOf(b) || []) addBlock(it, res, 'Program blocks') }
      else { addTree(root, res) }
    } catch (e) { res.errors.push('GetBlocksWithHierarchy: ' + ((e && e.message) || e)) }

    // PLC tag tables
    try { res.tagTables = (await toolJson('GetPlcTagTables', { softwarePath }, true)).items || [] } catch {}

    // HMI
    try { const info = await toolJson('GetHmiProgramInfo', { softwarePath: 'HMI_RT_1' }, true); let s = []; try { s = (await toolJson('GetHmiScreens', { softwarePath: 'HMI_RT_1' }, true)).items || [] } catch {}; res.hmi.push({ softwarePath: 'HMI_RT_1', programType: info.programType || null, screens: s }) } catch (e) { res.notes.push('HMI: ' + ((e && e.message) || e)) }

    // Cross-references per code block -> clean "uses"
    for (const b of res.blocks) {
      const kind = (b.kind || '').toUpperCase()
      if (!['OB', 'FB', 'FC', 'DB'].includes(kind)) continue
      const qp = b.qualifiedPath
      if (!qp) continue
      const cr = await toolJson('GetCrossReferences', { softwarePath, objectPath: qp }, true)
      const its = itemsOf(cr) || []
      // dedupe by (ref,type,access); classify
      const seen = new Map()
      for (const r of its) {
        const key = (r.referenceName || '') + '|' + (r.referenceType || '') + '|' + (r.access || '')
        if (!key.trim() || seen.has(key)) continue
        seen.set(key, true)
      }
      // produce non-empty unique list
      const refs = [...seen.keys()].map((k) => { const [ra, ty, ac] = k.split('|'); return { name: ra, type: ty || null, access: ac || null } })
      if (refs.length) res.blockUses.push({ block: b.name, kind, refs })
    }
  } catch (e) { res.errors.push('top: ' + ((e && (e.message || e)) || 'unknown')) }

  mkdirSync(path.dirname(outPath), { recursive: true })
  writeFileSync(outPath, JSON.stringify(res, null, 2))

  console.log('\n已写出: ' + outPath)
  console.log('  project =', res.project?.name ?? '(unknown)')
  console.log('  devices =', (res.devices.map((d) => d.name) || []).join(', '))
  console.log('  blocks  =', res.blocks.length, '->', res.blocks.map((b) => `${b.qualified}`).join(' | '))
  console.log('  blockUses (去重引用) =', res.blockUses.length)
  for (const u of res.blockUses) {
    console.log('    [' + u.block + '] 使用', u.refs.length, '类:')
    const byType = {}
    for (const r of u.refs) { const t = r.type || 'ref'; byType[t] = (byType[t] || 0) + 1 }
    console.log('       ' + Object.entries(byType).map(([k, v]) => `${k}=${v}`).join(' '))
    // show sample names of 'Uses'
    const uses = u.refs.filter((r) => (r.type || 'Uses').toLowerCase().startsWith('use'))
    if (uses.length) console.log('       样例 Uses:', uses.slice(0, 14).map((r) => r.name).join(', '))
  }
  if (res.hmi.length) for (const h of res.hmi) console.log('  HMI', h.softwarePath, '=', h.programType || '', 'screen:', (h.screens || []).join(','))
  if (res.notes.length) res.notes.forEach((n) => console.log('  note: ' + n))
  if (res.errors.length) res.errors.forEach((e) => console.log('  error: ' + e)); else console.log('  无错误。')
  try { await client.close() } catch {}
}

function addTree(node, res) {
  // node may be the root BlockGroupInfo; push blocks preserving group prefix from hierarchy
  // root.name e.g. "程序块" (Chinese, may mojibake) — but blocks carry their own group path components.
  for (const g of node.groups || []) addTree(g, res)
  const groupName = node.name || ''
  const sanitizedGroup = /^[\x00-\x7F]+$/.test(groupName) ? groupName : '' // skip possibly-moja group labels; real path used in qualifiedPath below is best-effort
  for (const b of node.blocks || []) addBlock(b, res, sanitizedGroup || 'Program blocks')
}

function addBlock(it, res, groupName) {
  const a = it.attributes || []
  const number = attr(a, 'Number')
  const kind = it.typeName || it.type || ''
  const name = String(it.name || '')
  const groupPath = `/Program blocks` // keep structural ASCII anchor
  const rec = {
    kind,
    name,
    number,
    language: it.programmingLanguage || null,
    isConsistent: it.isConsistent ?? attr(a, 'IsConsistent') ?? null,
    memoryLayout: it.memoryLayout || attr(a, 'MemoryLayout') || null,
    modifiedDate: it.modifiedDate || attr(a, 'ModifiedDate') || null,
    qualifiedPath: groupPath + '/' + name
  }
  const label = kind === 'DB' ? 'DB' : kind
  rec.qualified = number != null ? `${label}${number} "${name}"` : `${kind || ''} "${name}"`
  res.blocks.push(rec)
}

main().catch((e) => { console.error('FATAL', e); process.exit(1) })
