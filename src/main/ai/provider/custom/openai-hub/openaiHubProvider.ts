import { AnthropicMessagesLanguageModel } from '@ai-sdk/anthropic/internal'
import { GoogleGenerativeAILanguageModel } from '@ai-sdk/google/internal'
import {
  OpenAICompatibleChatLanguageModel,
  OpenAICompatibleEmbeddingModel,
  OpenAICompatibleImageModel
} from '@ai-sdk/openai-compatible'
import type { EmbeddingModelV3, ImageModelV3, LanguageModelV3, ProviderV3 } from '@ai-sdk/provider'
import type { FetchFunction } from '@ai-sdk/provider-utils'
import { loadApiKey, withoutTrailingSlash } from '@ai-sdk/provider-utils'
import { ENDPOINT_TYPE, type EndpointType } from '@shared/data/types/model'
import { formatApiHost, withoutTrailingApiVersion } from '@shared/utils/api'

import { resolveOpenAIHubChatFamily } from './openaiHubRouting'

export const OPENAI_HUB_PROVIDER_NAME = 'openai-hub' as const

export interface OpenAIHubProviderSettings {
  apiKey?: string
  /** Base URL selected for this request. When `endpointBaseURLs` is absent the
   *  google/chat URL helpers fall back to it. */
  baseURL?: string
  /** Explicit per-protocol URLs, decided at request time from the provider row. */
  endpointBaseURLs?: Partial<Record<EndpointType, string>>
  headers?: Record<string, string>
  fetch?: FetchFunction
}

export interface OpenAIHubProvider extends ProviderV3 {
  (modelId: string): LanguageModelV3
  languageModel(modelId: string): LanguageModelV3
  embeddingModel(modelId: string): EmbeddingModelV3
  imageModel(modelId: string): ImageModelV3
}

/**
 * Build the openai-hub provider. openai-hub is a multi-backend relay: one key,
 * one host, but a SEPARATE native wire endpoint per vendor (OpenAI `/v1` chat,
 * Anthropic `/v1/messages`, Google `/v1beta`). We dispatch the AI-SDK model
 * class from the bare model id (see openaiHubRouting) so Claude/Gemini are NOT
 * forced through the OpenAI-compatible chat path — that mismatch is what made
 * those models loop/error.
 */
export function createOpenAIHubProvider(settings: OpenAIHubProviderSettings = {}): OpenAIHubProvider {
  const { baseURL, fetch: customFetch } = settings
  if (!baseURL) {
    throw new Error(
      'openai-hub provider requires a non-empty `baseURL`. An empty value would resolve fetch paths against the renderer process origin (app://, file://) and surface as opaque "Failed to fetch" errors.'
    )
  }

  const resolveApiKey = () =>
    loadApiKey({ apiKey: settings.apiKey, environmentVariableName: 'OPENAI_HUB_API_KEY', description: 'OpenAI-Hub' })

  const compatHeaders = () => ({ Authorization: `Bearer ${resolveApiKey()}`, ...settings.headers })

  const chatBaseURL = settings.endpointBaseURLs?.[ENDPOINT_TYPE.OPENAI_CHAT_COMPLETIONS] ?? baseURL
  const compatUrl = ({ path }: { path: string; modelId: string }) => `${withoutTrailingSlash(chatBaseURL)}${path}`
  const nativeBaseURL = withoutTrailingApiVersion(baseURL)
  const anthropicBaseURL =
    settings.endpointBaseURLs?.[ENDPOINT_TYPE.ANTHROPIC_MESSAGES] ?? formatApiHost(nativeBaseURL, true)
  const geminiBaseURL =
    settings.endpointBaseURLs?.[ENDPOINT_TYPE.GOOGLE_GENERATE_CONTENT] ?? formatApiHost(nativeBaseURL, true, 'v1beta')

  const openaiChatModel: (modelId: string) => OpenAICompatibleChatLanguageModel = (modelId) =>
    new OpenAICompatibleChatLanguageModel(modelId, {
      provider: `${OPENAI_HUB_PROVIDER_NAME}.chat`,
      url: compatUrl,
      headers: compatHeaders,
      fetch: customFetch
    })

  const createChatModel = (modelId: string): LanguageModelV3 => {
    switch (resolveOpenAIHubChatFamily(modelId)) {
      case 'anthropic':
        return new AnthropicMessagesLanguageModel(modelId, {
          provider: `${OPENAI_HUB_PROVIDER_NAME}.anthropic`,
          baseURL: anthropicBaseURL,
          headers: () => ({ 'x-api-key': resolveApiKey(), ...settings.headers }),
          fetch: customFetch,
          supportedUrls: () => ({ 'image/*': [/^https?:\/\/.*$/] }),
          supportsNativeStructuredOutput: false
        })
      case 'gemini':
        return new GoogleGenerativeAILanguageModel(modelId, {
          provider: `${OPENAI_HUB_PROVIDER_NAME}.google`,
          baseURL: geminiBaseURL,
          // openai-hub authenticates Gemini chat with the same sk- Bearer key
          // (not x-goog-api-key) — per their native Gemini docs.
          headers: () => ({ Authorization: `Bearer ${resolveApiKey()}`, ...settings.headers }),
          fetch: customFetch,
          generateId: () => `${OPENAI_HUB_PROVIDER_NAME}-${Date.now()}`,
          supportedUrls: () => ({})
        })
      default:
        // gpt, gpt-image, o*, DeepSeek, Qwen, … — everything non-Claude-non-Gemini
        // is served by openai-hub on the OpenAI `/v1/chat/completions` shape.
        return openaiChatModel(modelId)
    }
  }

  const provider = (modelId: string) => createChatModel(modelId)
  provider.specificationVersion = 'v3' as const
  provider.languageModel = createChatModel
  provider.embeddingModel = (modelId: string): EmbeddingModelV3 =>
    new OpenAICompatibleEmbeddingModel(modelId, {
      provider: `${OPENAI_HUB_PROVIDER_NAME}.embedding`,
      url: compatUrl,
      headers: compatHeaders,
      fetch: customFetch
    })
  // Images aren't the reported fault and openai-hub's own image catalog is not
  // canonical here; expose a safe OpenAI-compatible image model so the provider
  // satisfies the ProviderV3 surface without inventing native image routing.
  provider.imageModel = (modelId: string): ImageModelV3 =>
    new OpenAICompatibleImageModel(modelId, {
      provider: `${OPENAI_HUB_PROVIDER_NAME}.image`,
      url: compatUrl,
      headers: compatHeaders,
      fetch: customFetch
    })

  return provider as OpenAIHubProvider
}
