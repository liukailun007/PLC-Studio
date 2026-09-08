/**
 * OpenAI-Hub per-model chat routing — single source of truth for the runtime
 * model class, endpoint protocol, and provider-options namespace.
 *
 * OpenAI-Hub is a multi-backend relay whose model ids carry NO vendor prefix
 * (`claude-sonnet-4-5`, `gemini-2.5-pro`, `gpt-4o`, `deepseek-chat`). Each
 * model must be sent to its vendor's NATIVE wire endpoint (its `/v1/chat`
 * path does NOT translate Claude/Gemini for you). This module derives the
 * chat family (→ endpoint + option namespace) from the raw model id, mirroring
 * how DMXAPI (`dmxapiRouting.ts`) dispatches the same families from bare ids.
 */
import { ENDPOINT_TYPE, type EndpointType } from '@shared/data/types/model'

export type OpenAIHubChatFamily = 'openai-compat' | 'anthropic' | 'gemini'

const CHAT_FAMILY_TABLE: Array<{
  family: Exclude<OpenAIHubChatFamily, 'openai-compat'>
  match: (modelId: string) => boolean
}> = [
  // Claude → Anthropic native `/v1/messages`.
  { family: 'anthropic', match: (id) => /claude/i.test(id) },
  {
    family: 'gemini',
    // Gemini chat models only (id contains `gemini`); keep image/TTS variants off
    // the native chat route.
    match: (id) => /gemini/i.test(id) && !/(image|imagen|tts|audio|embedding)/i.test(id)
  }
]

export function resolveOpenAIHubChatFamily(modelId: string): OpenAIHubChatFamily {
  return CHAT_FAMILY_TABLE.find((entry) => entry.match(modelId))?.family ?? 'openai-compat'
}

const FAMILY_ENDPOINT: Record<OpenAIHubChatFamily, EndpointType> = {
  anthropic: ENDPOINT_TYPE.ANTHROPIC_MESSAGES,
  gemini: ENDPOINT_TYPE.GOOGLE_GENERATE_CONTENT,
  'openai-compat': ENDPOINT_TYPE.OPENAI_CHAT_COMPLETIONS
}

const FAMILY_PROVIDER_OPTIONS_KEY: Record<OpenAIHubChatFamily, string> = {
  anthropic: 'anthropic',
  gemini: 'google',
  // @ai-sdk/openai-compatible reads this from the `openai-hub.chat` provider string.
  'openai-compat': 'openai-hub'
}

export interface OpenAIHubChatRoute {
  endpointType: EndpointType
  providerOptionsKey: string
}

export function resolveOpenAIHubChatRoute(modelId: string): OpenAIHubChatRoute {
  const family = resolveOpenAIHubChatFamily(modelId)
  return {
    endpointType: FAMILY_ENDPOINT[family],
    providerOptionsKey: FAMILY_PROVIDER_OPTIONS_KEY[family]
  }
}

export function resolveOpenAIHubEndpointType(modelId: string): EndpointType {
  return resolveOpenAIHubChatRoute(modelId).endpointType
}
