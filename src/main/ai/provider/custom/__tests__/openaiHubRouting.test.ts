import { ENDPOINT_TYPE } from '@shared/data/types/model'
import { describe, expect, it } from 'vitest'

import { resolveOpenAIHubChatFamily, resolveOpenAIHubChatRoute } from '../openai-hub/openaiHubRouting'

describe('resolveOpenAIHubChatFamily', () => {
  it.each([
    ['claude-sonnet-4-5', 'anthropic'],
    ['claude-opus-4.7', 'anthropic'],
    ['claude-3-opus-20240229', 'anthropic'],
    ['gemini-2.5-pro', 'gemini'],
    ['gemini-2.5-flash', 'gemini'],
    // Gemini variants fall to the OpenAI-compatible chat path for non-chat kinds.
    ['gemini-2.5-flash-image-preview', 'openai-compat'],
    // Everything else (incl. gpt / gpt-image / AI / Dall-E). openai-hub serves
    // GPT etc. through the same OpenAI `/v1/chat/completions` shape as DeepSeek/everything
    // non-Claude-non-Gemini, so they share the openai-compatible chat family.
    ['gpt-4o', 'openai-compat'],
    ['gpt-5.4', 'openai-compat'],
    ['o3', 'openai-compat'],
    ['deepseek-chat', 'openai-compat'],
    ['qwen3.5-plus', 'openai-compat']
  ] as const)('routes %s → %s', (modelId, family) => {
    expect(resolveOpenAIHubChatFamily(modelId)).toBe(family)
  })
})

describe('resolveOpenAIHubChatRoute', () => {
  it.each([
    ['claude-sonnet-4-5', ENDPOINT_TYPE.ANTHROPIC_MESSAGES, 'anthropic'],
    ['gemini-2.5-pro', ENDPOINT_TYPE.GOOGLE_GENERATE_CONTENT, 'google'],
    // gpt / deepseek / everything non-Claude-non-Gemini → OpenAI-compatible
    // chat (one native-format path per vendor: only Claude & Gemini split off).
    ['gpt-4o', ENDPOINT_TYPE.OPENAI_CHAT_COMPLETIONS, 'openai-hub'],
    ['deepseek-chat', ENDPOINT_TYPE.OPENAI_CHAT_COMPLETIONS, 'openai-hub']
  ] as const)('maps %s → %s / %s', (modelId, endpointType, providerOptionsKey) => {
    expect(resolveOpenAIHubChatRoute(modelId)).toEqual({ endpointType, providerOptionsKey })
  })
})
