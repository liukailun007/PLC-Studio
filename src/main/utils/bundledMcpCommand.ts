import path from 'node:path'

import { application } from '@application'

import { toAsarUnpackedPath } from './asar'

/**
 * Marker prefix for MCP server `command` values that point at a binary bundled
 * inside the app's own `resources/` directory (e.g. the TIA Portal Openness MCP
 * server shipped at `resources/tia-mcp/`).
 *
 * The stored command is a location-independent logical URI
 * (`cherry-resource://tia-mcp/v21/TiaMcpServer.exe`) instead of an absolute
 * path. This keeps the database row valid across installs: the installer may
 * place the app anywhere, portable builds may be moved, and v2 backup/restore
 * may land on a machine with a different install directory — resolving at
 * spawn time always yields the current bundled location.
 */
export const CHERRY_RESOURCE_PREFIX = 'cherry-resource://'

/** Whether an MCP server `command` references a bundled resource. */
export function isBundledMcpCommand(command: string): boolean {
  return command.startsWith(CHERRY_RESOURCE_PREFIX)
}

/**
 * Resolve an MCP server `command` to a spawnable filesystem path.
 *
 * - `cherry-resource://<relative>` → the file at `<app resources>/<relative>`,
 *   rewritten through the asar.unpacked layout in packaged builds so the
 *   spawned binary lives on disk next to its sibling files.
 * - Anything else → returned unchanged (regular commands, `npx`, URLs, etc.).
 */
export function resolveBundledMcpCommand(command: string): string {
  if (!isBundledMcpCommand(command)) {
    return command
  }
  const relative = command.slice(CHERRY_RESOURCE_PREFIX.length)
  return toAsarUnpackedPath(path.join(application.getPath('app.root.resources'), relative))
}
