#!/usr/bin/env node
/**
 * TIA ProjectContext — 按块查询 maps.json（省 token：只返回需要的那一段/那几块）。
 *
 * 用法（仓库根目录）：
 *   node scripts/tia-project-scanner/query.mjs block <name>         # 返回某一块(含成员/注释)
 *   node scripts/tia-project-scanner/query.mjs index                # 只返回紧凑索引(设备+块清单)，很小
 *   node scripts/tia-project-scanner/query.mjs uses <name>          # 暂显：该块引用的 operands
 *   node scripts/tia-project-scanner/query.mjs list                 # 列出块(等价 index 简短版)
 *   [--map <path>]  默认 out/project-context/maps.json
 *
 * 关键：只把"命中的块"打出到 stdout，让 AI 只要极小的查询结果，不整篇塞上下文。
 */
import { readFileSync } from 'node:fs'
import path from 'node:path'
import process from 'node:process'
import { fileURLToPath } from 'node:url'

const __dirname = path.dirname(fileURLToPath(import.meta.url))
const REPO_ROOT = path.resolve(__dirname, '..', '..')
const argVal = (n, d) => { const i = process.argv.indexOf(n); return i >= 0 && process.argv[i + 1] ? process.argv[i + 1] : d }
let mapPath = argVal('--map', path.join(REPO_ROOT, 'out', 'project-context', 'maps.json'))

const sub = process.argv[2]
const value = process.argv[3]
if (!sub) { console.error('USAGE: query.mjs <index|list|block <name>|uses <name>>'); process.exit(1) }

let j
try { j = JSON.parse(readFileSync(mapPath, 'utf8')) } catch (e) { console.error('无法读取 maps.json:', mapPath, e.message); process.exit(1) }

const blocks = j.blocks || []
const compactBlock = (b) => ({
  kind: b.kind, name: b.name, number: b.number, language: b.language,
  qualified: b.qualifiedBare || b.qualified, nMembers: (b.members || []).length
})

if (sub === 'index' || sub === 'list') {
  const out = {
    project: j.project && j.project.name, devices: (j.devices || []).map((d) => d.name),
    blocks: blocks.map(compactBlock)
  }
  const s = JSON.stringify(out)
  console.log(s)
  console.error('[query] index bytes=', Buffer.byteLength(s, 'utf8'), '(很小，建议 AI 只拉这个)')
  process.exit(0)
}

if (sub === 'block') {
  const name = value
  if (!name) { console.error('block <name> 需给出块名'); process.exit(1) }
  const b = blocks.find((x) => x.name === name || (x.qualifiedBare || '').includes(name))
  if (!b) { console.error('未找到块:', name, '。可用块:', blocks.map((x) => x.qualifiedBare).join('; ')); process.exit(1) }
  console.log(JSON.stringify(b, null, 2))
  process.exit(0)
}

if (sub === 'uses') {
  const name = value
  const b = blocks.find((x) => x.name === name)
  if (!b) { console.error('未找到块'); process.exit(1) }
  // approximate: list members grouped by direction
  const byDir = {}
  for (const m of b.members || []) { byDir[m.dir] = byDir[m.dir] || []; byDir[m.dir].push(m.name) }
  const out = { block: name, dirs: Object.fromEntries(Object.entries(byDir).map(([k, v]) => [k, v])) }
  console.log(JSON.stringify(out, null, 2))
  process.exit(0)
}

console.error('未知子命令:', sub)
process.exit(1)
