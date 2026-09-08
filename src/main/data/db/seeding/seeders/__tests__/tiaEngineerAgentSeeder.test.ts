import { agentTable } from '@data/db/schemas/agent'
import { agentMcpServerTable } from '@data/db/schemas/assistantRelations'
import { agentSessionTable } from '@data/db/schemas/agentSession'
import { agentWorkspaceTable } from '@data/db/schemas/agentWorkspace'
import { mcpServerTable } from '@data/db/schemas/mcpServer'
import { AGENT_WORKSPACE_TYPE } from '@shared/data/api/schemas/agentWorkspaces'
import { setupTestDatabase } from '@test-helpers/db'
import { eq } from 'drizzle-orm'
import { describe, expect, it } from 'vitest'

import { TIA_ENGINEER_AGENT_NAME, TIA_ENGINEER_INSTRUCTIONS, TiaEngineerAgentSeeder } from '../tiaEngineerAgentSeeder'

describe('TiaEngineerAgentSeeder', () => {
  const dbh = setupTestDatabase()

  function tiaAgents() {
    return dbh.db.select().from(agentTable).where(eq(agentTable.name, TIA_ENGINEER_AGENT_NAME)).all()
  }

  function seedTiaMcpServer(): string {
    const id = 'tia-mcp-server-id'
    dbh.db
      .insert(mcpServerTable)
      .values({
        id,
        name: 'TIA Portal MCP (V21)',
        type: 'stdio',
        description: 'Siemens TIA Portal Openness MCP server (V21)',
        command: 'C:\\tia\\TiaMcpServer.exe',
        args: [],
        env: {}
      })
      .run()
    return id
  }

  it('creates the TIA engineer agent with the full instructions and a system session', () => {
    seedTiaMcpServer()

    new TiaEngineerAgentSeeder().run(dbh.db)

    const [agent] = tiaAgents()
    expect(agent).toBeDefined()
    expect(agent.type).toBe('claude-code')
    expect(agent.model).toBeNull()
    expect(agent.instructions).toBe(TIA_ENGINEER_INSTRUCTIONS)
    expect(agent.instructions).toContain('TIA Portal MCP (V21) 调用规约')
    expect(agent.instructions).toContain('writeplcsclsourcefile')

    const [session] = dbh.db.select().from(agentSessionTable).where(eq(agentSessionTable.agentId, agent.id)).all()
    expect(session).toMatchObject({ agentId: agent.id, name: '' })
    const [workspace] = dbh.db
      .select()
      .from(agentWorkspaceTable)
      .where(eq(agentWorkspaceTable.id, session.workspaceId))
      .all()
    expect(workspace).toMatchObject({ type: AGENT_WORKSPACE_TYPE.SYSTEM })
  })

  it('binds the TIA MCP server when it exists', () => {
    const serverId = seedTiaMcpServer()

    new TiaEngineerAgentSeeder().run(dbh.db)

    const [agent] = tiaAgents()
    const [binding] = dbh.db.select().from(agentMcpServerTable).where(eq(agentMcpServerTable.agentId, agent.id)).all()
    expect(binding).toMatchObject({ agentId: agent.id, mcpServerId: serverId })
  })

  it('still creates the agent without a binding when the TIA MCP server is absent', () => {
    new TiaEngineerAgentSeeder().run(dbh.db)

    const [agent] = tiaAgents()
    expect(agent).toBeDefined()
    const bindings = dbh.db.select().from(agentMcpServerTable).where(eq(agentMcpServerTable.agentId, agent.id)).all()
    expect(bindings).toHaveLength(0)
  })

  it('is idempotent on re-run', () => {
    seedTiaMcpServer()
    const seeder = new TiaEngineerAgentSeeder()

    seeder.run(dbh.db)
    seeder.run(dbh.db)

    expect(tiaAgents()).toHaveLength(1)
  })
})
