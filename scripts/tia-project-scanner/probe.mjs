#!/usr/bin/env node
/**
 * TIA MCP —— 一次性探测脚本（只读）。用 @modelcontextprotocol/sdk 连本机 TIA Openness，
 * 打开项目3，调用一组只读工具，打印真实 .text 响应 + 把所有原始响应存 JSONL。
 * 绝不写入 / 修改目标项目。
 *
 *   node scripts/tia-project-scanner/probe.mjs --project "....项目3.ap21"
 */
import { mkdirSync, appendFileSync } from 'node:fs'
import path from 'node:path'
import process from 'node:process'
import { fileURLToPath } from 'node:url'
import { Client } from '@modelcontextprotocol/sdk/client/index.js'
import { StdioClientTransport } from '@modelcontextprotocol/sdk/client/stdio.js'

const __dirname = path.dirname(fileURLToPath(import.meta.url))
const REPO_ROOT = path.resolve(__dirname, '..', '..')
const argVal = (n, d) => { const i = process.argv.indexOf(n); return i >= 0 && process.argv[i + 1] ? process.argv[i + 1] : d }
const project = argVal('--project', 'C:\\Users\\Administrator\\Desktop\\项目3\\项目3.ap21')
const portal = argVal('--portal', 'C:\\Program Files\\Siemens\\Automation\\Portal V21')
const EXE = path.join(REPO_ROOT, 'MCP', 'TIA_Portal_Openness_MCP-master', 'runtime', 'v21', 'TiaMcpServer.exe')
const OUT = argVal('--out', path.join(REPO_ROOT, 'out', 'project-context', 'probe_out.jsonl'))

const transport = new StdioClientTransport({
  command: EXE,
  args: ['--tia-portal-location', portal, '--tia-major-version', '21', '--logging', '0'],
  stderr: 'pipe'
})
transport.stderr?.on('data', (d) => { const s = d.toString().trim(); if (s) console.error('[stderr]', s.slice(0, 300)) })
const client = new Client({ name: 'tia-probe', version: '0.1' })

async function toolText(r) {
  const c = (r && r.content) || []
  return c.map((x) => (x && typeof x.text === 'string') ? x.text : JSON.stringify(x)).join('\n').trim()
}

async function main() {
  await client.connect(transport)
  console.log('connected; listing tools')
  const tl = await client.listTools()
  const tools = tl.tools || []
  console.log('tool count =', tools.length)
  mkdirSync(path.dirname(OUT), { recursive: true })
  appendFileSync(OUT, '')

  async function call(name, args = {}) {
    console.log('\n===== call', name, '=====')
    let r
    try { r = await client.callTool({ name, arguments: args }) }
    catch (e) { console.log('  <ERROR>', e && e.message); appendFileSync(OUT, JSON.stringify({ tool: name, error: String(e && e.message || e) }) + '\n'); return }
    const text = await toolText(r)
    console.log(text.slice(0, 2500))
    appendFileSync(OUT, JSON.stringify({ tool: name, text }) + '\n')
    return { text }
  }

  await call('GetProject')
  await call('GetProjectTree')
  await call('GetDevices')
  await call('GetSoftwareInfo', { softwarePath: 'PLC_1' })
  await call('GetSoftwareTree', { softwarePath: 'PLC_1' })
  await call('GetBlocksWithHierarchy', { softwarePath: 'PLC_1' })
  await call('GetBlocks', { softwarePath: 'PLC_1', name: '' })
  await call('GetPlcTagTables', { softwarePath: 'PLC_1' })
  await call('GetHmiProgramInfo', { softwarePath: 'HMI_RT_1' })
  console.log('\nRaw saved to', OUT)
  try { await client.close() } catch {}
}
main().catch((e) => { console.error('FATAL', e); process.exit(1) })
