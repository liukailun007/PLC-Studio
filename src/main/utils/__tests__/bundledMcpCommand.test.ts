import path from 'node:path'

import { describe, expect, it } from 'vitest'

import { CHERRY_RESOURCE_PREFIX, isBundledMcpCommand, resolveBundledMcpCommand } from '../bundledMcpCommand'

// @application is globally mocked (tests/main.setup.ts): getPath('app.root.resources')
// returns '/mock/app.root.resources'. toAsarUnpackedPath passes through because the
// mocked electron app has no `isPackaged` (falsy) in tests.

describe('bundledMcpCommand', () => {
  describe('isBundledMcpCommand', () => {
    it('returns true for cherry-resource:// commands', () => {
      expect(isBundledMcpCommand(`${CHERRY_RESOURCE_PREFIX}tia-mcp/v21/TiaMcpServer.exe`)).toBe(true)
    })

    it('returns false for regular commands', () => {
      expect(isBundledMcpCommand('npx')).toBe(false)
      expect(isBundledMcpCommand('C:\\tools\\server.exe')).toBe(false)
      expect(isBundledMcpCommand('')).toBe(false)
    })
  })

  describe('resolveBundledMcpCommand', () => {
    it('resolves a marker against the app resources root', () => {
      const expected = path.join('/mock/app.root.resources', 'tia-mcp/v21/TiaMcpServer.exe')

      expect(resolveBundledMcpCommand(`${CHERRY_RESOURCE_PREFIX}tia-mcp/v21/TiaMcpServer.exe`)).toBe(expected)
    })

    it('passes non-marker commands through unchanged', () => {
      const plain = 'npx -y @some/server'

      expect(resolveBundledMcpCommand(plain)).toBe(plain)
    })
  })
})
