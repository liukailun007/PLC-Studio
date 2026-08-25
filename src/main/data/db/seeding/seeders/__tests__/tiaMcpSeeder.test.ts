import type * as NodeFS from 'node:fs'
import fs from 'node:fs'

import { mcpServerTable } from '@data/db/schemas/mcpServer'
import { resolveBundledMcpCommand } from '@main/utils/bundledMcpCommand'
import { setupTestDatabase } from '@test-helpers/db'
import { eq } from 'drizzle-orm'
import { afterEach, describe, expect, it, vi } from 'vitest'

import {
  TIA_MCP_COMMAND,
  TIA_MCP_DEFAULT_ARGS,
  TIA_MCP_LONG_RUNNING,
  TIA_MCP_SERVER_NAME,
  TIA_MCP_TIMEOUT_SECONDS
} from '../tiaMcpSeeder'

// @application and @logger are globally mocked (tests/main.setup.ts), so the real
// resolveBundledMcpCommand() maps the marker to /mock/app.root.resources/...
// `node:fs` is overridden here (the global setup passes it through untouched) so the
// seeder's existsSync bundle check can be controlled per test; every other fs
// function still delegates to the real implementation (the DB harness needs them).
vi.mock('node:fs', async () => {
  const actual = await vi.importActual<typeof NodeFS>('node:fs')
  const existsSync = vi.fn(actual.existsSync)
  return {
    ...actual,
    existsSync,
    default: { ...actual, existsSync }
  }
})

// `@main/core/platform` is NOT globally mocked — loadSeeder() re-imports the seeder
// under a per-test platform mock (same pattern as utils/__tests__/windowUtil.test.ts).
async function loadSeeder({ isWin }: { isWin: boolean }) {
  vi.resetModules()
  vi.doMock('@main/core/platform', () => ({ isWin }))
  const { TiaMcpSeeder } = await import('../tiaMcpSeeder')
  return new TiaMcpSeeder()
}

describe('TiaMcpSeeder', () => {
  const dbh = setupTestDatabase()
  const resolvedExe = resolveBundledMcpCommand(TIA_MCP_COMMAND)

  afterEach(() => {
    vi.mocked(fs.existsSync).mockClear()
    vi.restoreAllMocks()
    vi.resetModules()
    vi.doUnmock('@main/core/platform')
  })

  it('inserts the TIA MCP server once with the resolved command path (Windows)', async () => {
    vi.mocked(fs.existsSync).mockReturnValue(true)
    const seeder = await loadSeeder({ isWin: true })

    seeder.run(dbh.db)
    seeder.run(dbh.db) // re-run must stay idempotent

    const rows = await dbh.db.select().from(mcpServerTable)
    expect(rows).toHaveLength(1)
    expect(rows[0].name).toBe(TIA_MCP_SERVER_NAME)
    expect(rows[0].type).toBe('stdio')
    expect(rows[0].command).toBe(resolvedExe)
    expect(rows[0].args).toEqual(TIA_MCP_DEFAULT_ARGS)
    expect(rows[0].env).toEqual({})
    expect(rows[0].isActive).toBe(false)
    expect(rows[0].installSource).toBe('builtin')
    expect(rows[0].isTrusted).toBe(true)
    // Hardware-catalog tools need more than the client's 60 s default budget.
    expect(rows[0].longRunning).toBe(TIA_MCP_LONG_RUNNING)
    expect(rows[0].timeout).toBe(TIA_MCP_TIMEOUT_SECONDS)
  })

  it('repairs a v1 marker command on an untouched builtin row', async () => {
    vi.mocked(fs.existsSync).mockReturnValue(true)
    await dbh.db.insert(mcpServerTable).values({
      id: 'srv-v1',
      name: TIA_MCP_SERVER_NAME,
      type: 'stdio',
      command: TIA_MCP_COMMAND, // what the v1 seeder stored
      args: [...TIA_MCP_DEFAULT_ARGS],
      env: {},
      isActive: false,
      installSource: 'builtin'
    })

    const seeder = await loadSeeder({ isWin: true })
    seeder.run(dbh.db)

    const [row] = await dbh.db.select().from(mcpServerTable).where(eq(mcpServerTable.name, TIA_MCP_SERVER_NAME))
    expect(row.command).toBe(resolvedExe)
  })

  it('back-fills longRunning/timeout on an existing builtin row that never set them', async () => {
    vi.mocked(fs.existsSync).mockReturnValue(true)
    await dbh.db.insert(mcpServerTable).values({
      id: 'srv-bf',
      name: TIA_MCP_SERVER_NAME,
      type: 'stdio',
      command: resolvedExe,
      args: [...TIA_MCP_DEFAULT_ARGS],
      env: {},
      isActive: false,
      installSource: 'builtin',
      isTrusted: true,
      // timeout is intentionally null (fresh install from an older build)
      longRunning: null,
      timeout: null
    })

    const seeder = await loadSeeder({ isWin: true })
    seeder.run(dbh.db)

    const [row] = await dbh.db.select().from(mcpServerTable).where(eq(mcpServerTable.name, TIA_MCP_SERVER_NAME))
    expect(row.longRunning).toBe(TIA_MCP_LONG_RUNNING)
    expect(row.timeout).toBe(TIA_MCP_TIMEOUT_SECONDS)
  })

  it('does not overwrite a user-configured timeout on a builtin row', async () => {
    vi.mocked(fs.existsSync).mockReturnValue(true)
    await dbh.db.insert(mcpServerTable).values({
      id: 'srv-user-timeout',
      name: TIA_MCP_SERVER_NAME,
      type: 'stdio',
      command: resolvedExe,
      args: [...TIA_MCP_DEFAULT_ARGS],
      env: {},
      isActive: false,
      installSource: 'builtin',
      isTrusted: true,
      longRunning: false,
      timeout: 10
    })

    const seeder = await loadSeeder({ isWin: true })
    seeder.run(dbh.db)

    const [row] = await dbh.db.select().from(mcpServerTable).where(eq(mcpServerTable.name, TIA_MCP_SERVER_NAME))
    expect(row.longRunning).toBe(false)
    expect(row.timeout).toBe(10)
  })

  it('does not overwrite a user-customized command on a builtin row', async () => {
    vi.mocked(fs.existsSync).mockReturnValue(true)
    await dbh.db.insert(mcpServerTable).values({
      id: 'srv-custom',
      name: TIA_MCP_SERVER_NAME,
      type: 'stdio',
      command: 'C:\\custom\\TiaMcpServer.exe',
      args: [],
      env: {},
      isActive: false,
      installSource: 'builtin'
    })

    const seeder = await loadSeeder({ isWin: true })
    seeder.run(dbh.db)

    const [row] = await dbh.db.select().from(mcpServerTable).where(eq(mcpServerTable.name, TIA_MCP_SERVER_NAME))
    expect(row.command).toBe('C:\\custom\\TiaMcpServer.exe')
  })

  it('does not seed when the bundled runtime is missing', async () => {
    vi.mocked(fs.existsSync).mockReturnValue(false)
    const seeder = await loadSeeder({ isWin: true })

    seeder.run(dbh.db)

    const rows = await dbh.db.select().from(mcpServerTable)
    expect(rows).toHaveLength(0)
  })

  it('does not seed on non-Windows platforms', async () => {
    vi.mocked(fs.existsSync).mockReturnValue(true)
    const seeder = await loadSeeder({ isWin: false })

    seeder.run(dbh.db)

    const rows = await dbh.db.select().from(mcpServerTable)
    expect(rows).toHaveLength(0)
  })

  it('leaves an existing manually-installed row with the same name untouched', async () => {
    vi.mocked(fs.existsSync).mockReturnValue(true)
    await dbh.db.insert(mcpServerTable).values({
      id: 'user-srv-1',
      name: TIA_MCP_SERVER_NAME,
      type: 'stdio',
      command: 'C:\\custom\\TiaMcpServer.exe',
      args: [],
      env: {},
      isActive: true,
      installSource: 'manual'
    })

    const seeder = await loadSeeder({ isWin: true })
    seeder.run(dbh.db)

    const [row] = await dbh.db.select().from(mcpServerTable).where(eq(mcpServerTable.name, TIA_MCP_SERVER_NAME))
    expect(row.command).toBe('C:\\custom\\TiaMcpServer.exe')
    expect(row.installSource).toBe('manual')
    expect(row.isActive).toBe(true)
  })
})
