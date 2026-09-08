#!/usr/bin/env node
/**
 * TIA ProjectContext Scanner v0.4 (只读) —— 生成一份结构化“项目上下文”关系地图。
 *
 * 能力：
 *   - 设备(devices)、程序块(blocks, 含编号/类型/语言/一致性)
 *   - 逐块接口变量枚举：导出 SIMATIC 文档(.s7dcl+.s7res) → 解析每个 FB/FC/DB 的
 *     {方向 INPUT/OUTPUT/IN_OUT/STATIC/TEMP, 名, 类型, 中文注释(取自 .s7res)}
 *   - 可选 SCL 逻辑预览(直接 UTF-8 读文件，避开 MCP 乱码 bug)
 *   - HMI 概况
 *
 * 绝不写入/修改目标项目。块会只读导出到系统临时目录解析后自动清理。
 *
 * 用法（仓库根目录，需能用 node + 仓库 node_modules 解析 MCP SDK）：
 *   node scripts/tia-project-scanner/scan.mjs \
 *        --project "<...ap21>" \
 *        [--out out/project-context/maps.json] \
 *        [--portal "<Portal V21>"] [--with-logic] [--export-dir <dir>] [--verbose]
 */
import { mkdtempSync, readdirSync, readFileSync, rmSync, mkdirSync, writeFileSync } from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import process from 'node:process'
import { fileURLToPath } from 'node:url'
import { Client } from '@modelcontextprotocol/sdk/client/index.js'
import { StdioClientTransport } from '@modelcontextprotocol/sdk/client/stdio.js'
import { parseS7dcl, logicPreview } from './parseS7dcl.mjs'

const __dirname = path.dirname(fileURLToPath(import.meta.url))
const REPO_ROOT = path.resolve(__dirname, '..', '..')
const argVal = (n, d) => { const i = process.argv.indexOf(n); return i >= 0 && process.argv[i + 1] ? process.argv[i + 1] : d }
const has = (n) => process.argv.includes(n)
const verbose = has('--verbose')
const withLogic = has('--with-logic')
const project = argVal('--project', null)
const portal = argVal('--portal', 'C:\\Program Files\\Siemens\\Automation\\Portal V21')
const outPath = argVal('--out', path.join(REPO_ROOT, 'out', 'project-context', 'maps.json'))
const EXE = path.join(REPO_ROOT, 'MCP', 'TIA_Portal_Openness_MCP-master', 'runtime', 'v21', 'TiaMcpServer.exe')
const exportDirArg = has('--export-dir') ? argVal('--export-dir', null) : null

if (!project) { console.error('USAGE: node scan.mjs --project <...ap21> [--out maps.json] [--portal <Portal V21>]'); process.exit(1) }
const vlog = (...a) => { if (verbose) console.log('  >', ...a) }

const transport = new StdioClientTransport({
  command: EXE,
  args: ['--tia-portal-location', portal, '--tia-major-version', '21', '--logging', '0'],
  stderr: 'pipe'
})
transport.stderr?.on('data', (d) => { const s = d.toString().trim(); if (verbose && s) vlog('stderr:', s.slice(0, 250)) })
const client = new Client({ name: 'tia-project-context-scanner', version: '0.4.0' })

const attr = (a, n) => { const x = (a || []).find((y) => y.name === n); return x ? x.value : null }
const itemsOf = (o) => (o && (Array.isArray(o.items) ? o.items : Array.isArray(o.Items) ? o.Items : null)) || null

async function toolAny(name, args = {}) {
  vlog('call', name, JSON.stringify(args))
  try {
    const res = await client.callTool({ name, arguments: args })
    const c = (res && res.content) || []
    let text = ''
    for (const x of c) if (x && typeof x.text === 'string') text += x.text
    if (res && res.isError) throw new Error(name + ': ' + (text || 'isError'))
    try { return JSON.parse(text) } catch { return { rawText: text } }
  } catch (e) { return { error: (e && e.message) || String(e) } }
}

async function exportAllBlocks(softwarePath, groupPath, dir) {
  const r = await toolAny('ExportBlocksAsDocuments', { softwarePath, blockPath: groupPath, exportPath: dir })
  if (r.error) { vlog('batch export error:', r.error); return { error: r.error } }
  return { error: null }
}

async function main() {
  console.log('TIA ProjectContext Scanner v0.4 (只读)')
  console.log('  project:', project)
  await client.connect(transport)
  console.log('  connected.')

  const result = {
    meta: { scanner: 'tia-project-context-scanner v0.4', scannedAt: new Date().toISOString(), projectFile: project },
    project: { name: null, path: project },
    devices: [],
    blocks: [],
    hmi: [],
    notes: [],
    errors: []
  }

  let keepDir = null
  if (exportDirArg) { try { mkdirSync(exportDirArg, { recursive: true }); keepDir = exportDirArg } catch {} }
  const expDir = keepDir || mkdtempSync(path.join(os.tmpdir(), 'tia_pc_'))

  try {
    try { const pj = await toolAny('GetProject'); const its = itemsOf(pj); if (its && its[0]) result.project.name = its[0].name } catch {}

    try {
      const dv = await toolAny('GetDevices')
      for (const it of itemsOf(dv) || []) { const a = it.attributes || []; result.devices.push({ name: it.name, typeName: attr(a, 'TypeName') || null, typeIdentifier: attr(a, 'TypeIdentifier') || null }) }
    } catch (e) { result.errors.push('GetDevices: ' + ((e && e.message) || e)) }

    const softwarePath = 'PLC_1'
    try {
      const wh = await toolAny('GetBlocksWithHierarchy', { softwarePath })
      const root = wh && (wh.root || wh.Root)
      if (root) collectBlocks(root, result)
      else { const b = await toolAny('GetBlocks', { softwarePath, name: '' }); for (const it of itemsOf(b) || []) addBlockMeta(it, result) }
    } catch (e) { result.errors.push('GetBlocksWithHierarchy: ' + ((e && e.message) || e)) }

    try { const info = await toolAny('GetHmiProgramInfo', { softwarePath: 'HMI_RT_1' }); let s = []; try { s = (await toolAny('GetHmiScreens', { softwarePath: 'HMI_RT_1' })).items || [] } catch {}; result.hmi.push({ softwarePath: 'HMI_RT_1', programType: info.programType || null, screens: s }) } catch {}

    if (result.blocks.length) {
      console.log('  导出程序块文档以解析接口变量 …')
      const g = 'Program blocks'
      const exp = await exportAllBlocks(softwarePath, g, expDir)
      if (exp.error) result.errors.push('ExportBlocksAsDocuments: ' + exp.error)
      const byName = new Map()
      for (const f of readdirSync(expDir)) if (f.endsWith('.s7dcl')) byName.set(f.slice(0, -'.s7dcl'.length), f)
      for (const [base, fname] of byName) {
        let dcl
        try { dcl = readFileSync(path.join(expDir, fname), 'utf8') } catch { continue }
        let resText = ''
        try { resText = readFileSync(path.join(expDir, base + '.s7res'), 'utf8') } catch {}
        const blk = result.blocks.find((b) => b.name === base)
        if (!blk) continue
        const p = parseS7dcl(dcl, resText)
        if (p.kind) blk.kind = p.kind
        blk.members = p.members
        if (withLogic) { const lp = logicPreview(dcl, 7000); if (lp) blk.logicPreview = lp }
      }
      if (!byName.size && !keepDir) result.notes.push('导出未产生 .s7dcl（可能 LAD-only 或无接口变量）')
    }
  } catch (e) {
    result.errors.push('top: ' + ((e && (e.message || e)) || String(e)))
  } finally {
    if (!keepDir) { try { rmSync(expDir, { recursive: true, force: true }) } catch {} }
  }

  mkdirSync(path.dirname(outPath), { recursive: true })
  writeFileSync(outPath, JSON.stringify(result, null, 2))
  console.log('\n已写出: ' + outPath)
  console.log('  project =', result.project?.name ?? '(unknown)')
  console.log('  devices =', (result.devices.map((d) => d.name) || []).join(', '))
  console.log('  blocks  =', result.blocks.length)
  const byK = {}
  for (const b of result.blocks) byK[b.kind] = (byK[b.kind] || 0) + 1
  console.log('    分类: ' + Object.entries(byK).map(([k, v]) => `${k}=${v}`).join(', '))
  for (const b of result.blocks) {
    const n = (b.members || []).length
    const q = b.qualifiedBare || b.qualified || ''
    console.log(`      - ${q}  [${b.language || ''}]  变量=${n}${b.logicPreview ? ' ·逻辑✓' : ''}`)
    const mem = (b.members || []).slice(0, 6).map((m) => `${m.name}:${m.type}`).join(', ')
    if (mem) console.log(`          ${mem}${(b.members || []).length > 6 ? ', …' : ''}`)
  }
  for (const h of result.hmi) if (h.screens) console.log('  HMI', h.softwarePath, '=', h.programType || '', 'screen:', h.screens.join(','))
  if (result.notes.length) result.notes.forEach((n) => console.log('  note:', n))
  if (result.errors.length) result.errors.forEach((e) => console.log('  error:', e)); else console.log('  无错误。')
  try { await client.close() } catch {}
}

function collectBlocks(node, result) {
  for (const g of node.groups || []) collectBlocks(g, result)
  const name = node.name || ''
  const groupName = /^[\x00-\x7F]+$/.test(name) ? name : 'Program blocks'
  for (const b of node.blocks || []) addBlockMeta(b, result, groupName)
}

function addBlockMeta(it, result, groupName = 'Program blocks') {
  const a = it.attributes || []
  const number = attr(a, 'Number')
  const kind = it.typeName || it.type || ''
  const name = it.name
  const label = kind === 'DB' ? 'DB' : kind
  result.blocks.push({
    kind,
    name,
    number,
    language: it.programmingLanguage || null,
    group: name === 'Program blocks' ? '' : groupName,
    isConsistent: it.isConsistent ?? attr(a, 'IsConsistent') ?? null,
    memoryLayout: it.memoryLayout || attr(a, 'MemoryLayout') || null,
    modifiedDate: it.modifiedDate || attr(a, 'ModifiedDate') || null,
    qualifiedPath: `${groupName}/${name}`,
    qualifiedBare: number != null ? `${label}${number} "${name}"` : `${kind || ''} "${name}"`,
    members: []
  })
}

main().catch((e) => { console.error('FATAL', e); if (e && e.stack) console.error(e.stack); process.exit(1) })
