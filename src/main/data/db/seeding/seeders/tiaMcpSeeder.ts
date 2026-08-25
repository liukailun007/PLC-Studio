import fs from 'node:fs'

import { mcpServerTable } from '@data/db/schemas/mcpServer'
import { loggerService } from '@logger'
import { isWin } from '@main/core/platform'
import { CHERRY_RESOURCE_PREFIX, resolveBundledMcpCommand } from '@main/utils/bundledMcpCommand'
import { eq } from 'drizzle-orm'

import type { DbType, ISeeder } from '../../types'

const logger = loggerService.withContext('TiaMcpSeeder')

/** Display name of the seeded server (shown in the MCP settings list). */
export const TIA_MCP_SERVER_NAME = 'TIA Portal MCP (V21)'

/**
 * TIA Openness operations that touch the hardware catalog (SearchHardwareCatalog,
 * AddDeviceWithFallback, AddHardwareCatalogDeviceWithProbe) routinely exceed a generic
 * MCP tool's default time budget: building the catalog / enumerating all matching entries
 * can take well over a minute on a large or partially-loaded installation. Without an
 * explicit timeout the client's 60 s default kills the call mid-search (surfacing as
 * `-32001: Request timed out`).
 *
 * We opt the bundled server into `longRunning` (client resets its timeout on progress and
 * raises `maxTotalTimeout`), plus a generous per-call budget. 300 s is enough headroom for
 * hardware-catalog enumeration on slow first-load while staying well under the 10-minute
 * long-running cap enforced by `McpRuntimeService.callToolByServer`.
 */
export const TIA_MCP_TIMEOUT_SECONDS = 300
export const TIA_MCP_LONG_RUNNING = true

/**
 * Location-independent command marker for the bundled runtime. The seeder
 * resolves it to the real on-disk path (see {@link resolveBundledMcpCommand})
 * and stores THAT path — so the MCP settings page shows a normal filesystem
 * path instead of this internal marker.
 */
export const TIA_MCP_COMMAND = `${CHERRY_RESOURCE_PREFIX}tia-mcp/v21/TiaMcpServer.exe`

/**
 * Default launch arguments. `--tia-portal-location` points at the TIA Portal V21
 * Openness API installation and is user-customizable afterwards via the MCP
 * settings form (it is a default, not a fixed value).
 */
export const TIA_MCP_DEFAULT_ARGS = [
  '--tia-portal-location',
  'C:\\Program Files\\Siemens\\Automation\\Portal V21',
  '--tia-major-version',
  '21'
]

/**
 * Seed the bundled TIA Portal Openness MCP server (Siemens TIA Portal V21).
 *
 * Windows-only: the server is a .NET Framework executable that drives the local
 * TIA Portal Openness API, so seeding is skipped on other platforms and when
 * the bundled runtime is absent.
 *
 * The stored `command` is the resolved on-disk path (e.g.
 * `<app>/resources/app.asar.unpacked/resources/tia-mcp/v21/TiaMcpServer.exe`),
 * NOT the `cherry-resource://` marker — the settings page shows a real path.
 *
 * Insert-only by default, but the seeder repairs rows a previous version wrote
 * with the `cherry-resource://` marker: an untouched builtin row whose command
 * is still that marker is rewritten to the current resolved path. A
 * user-customized command is never overwritten.
 */
export class TiaMcpSeeder implements ISeeder {
  readonly name = 'tiaMcp'
  // v1 seeded the `cherry-resource://` marker; v2 seeds the resolved path.
  readonly version = '2'
  readonly description = 'Insert the bundled TIA Portal Openness MCP server (Windows only)'

  run(db: DbType): void {
    if (!isWin) {
      return
    }

    // Skip when the bundled runtime is missing (e.g. a dev checkout without it).
    const exePath = resolveBundledMcpCommand(TIA_MCP_COMMAND)
    if (!fs.existsSync(exePath)) {
      logger.warn('Bundled TIA MCP runtime missing, skipping seed', { exePath })
      return
    }

    const [existing] = db
      .select()
      .from(mcpServerTable)
      .where(eq(mcpServerTable.name, TIA_MCP_SERVER_NAME))
      .limit(1)
      .all()

    if (existing) {
      // Repair a row our v1 seeder wrote with the marker command: rewrite it to
      // the real on-disk path. Only when it is still an untouched builtin row
      // whose command is the marker — a user-customized command is never touched.
      if (existing.installSource === 'builtin' && existing.command === TIA_MCP_COMMAND) {
        db.update(mcpServerTable).set({ command: exePath }).where(eq(mcpServerTable.id, existing.id)).run()
      }

      // Back-fill the long-running / timeout defaults on a builtin row that never
      // had them set (timeout is null = the user has not explicitly configured one).
      // This lets an existing install pick up the hardware-catalog fix without a full
      // re-seed, while never clobbering a timeout the user deliberately configured.
      if (existing.installSource === 'builtin' && existing.timeout == null) {
        db.update(mcpServerTable)
          .set({ longRunning: TIA_MCP_LONG_RUNNING, timeout: TIA_MCP_TIMEOUT_SECONDS })
          .where(eq(mcpServerTable.id, existing.id))
          .run()
      }
      return
    }

    const now = Date.now()
    db.insert(mcpServerTable)
      .values({
        name: TIA_MCP_SERVER_NAME,
        type: 'stdio',
        description: 'Siemens TIA Portal Openness MCP server (V21)',
        command: exePath,
        args: [...TIA_MCP_DEFAULT_ARGS],
        env: {},
        isActive: false,
        installSource: 'builtin',
        isTrusted: true,
        trustedAt: now,
        installedAt: now,
        longRunning: TIA_MCP_LONG_RUNNING,
        timeout: TIA_MCP_TIMEOUT_SECONDS
      })
      .run()
  }
}
