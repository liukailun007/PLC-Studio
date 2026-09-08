import { defineProvider } from './types'

/**
 * OpenAI-Hub is a multi-backend relay: one API key, one host
 * (`api.openai-hub.net`), but a SEPARATE native wire endpoint per vendor
 * family. Model ids carry no factory prefix (`claude-sonnet-4-5`,
 * `gemini-2.5-pro`, `gpt-4o`, `deepseek-chat`), so the runtime routes each
 * chat model to its family's endpoint by id prefix (see
 * `src/main/ai/provider/custom/openai-hub/openaiHubRouting.ts`).
 *
 *  - OpenAI native     → `POST /v1/chat/completions`  (Bearer)
 *  - Anthropic native  → `POST /v1/messages`          (requires `anthropic-version`)
 *  - Google native     → `POST /v1beta/models/{model}:generateContent` (Bearer)
 *
 * Only `openai-chat-completions` is pre-registered in legacy configs; the
 * other two exist so Claude/Gemini models are NOT pushed through the OpenAI
 * `/v1/chat` path (which the relay does not translate for them).
 */
export default defineProvider({
  id: 'openai-hub',
  name: 'OpenAI-Hub',
  defaultChatEndpoint: 'openai-chat-completions',
  endpointConfigs: {
    'anthropic-messages': {
      adapterFamily: 'openai-hub',
      // AnthropicMessagesLanguageModel appends `/v1/messages`.
      baseUrl: 'https://api.openai-hub.net'
    },
    'google-generate-content': {
      adapterFamily: 'openai-hub',
      // GoogleGenerativeAILanguageModel appends `/models/{model}:generateContent`.
      baseUrl: 'https://api.openai-hub.net/v1beta/'
    },
    'openai-chat-completions': {
      adapterFamily: 'openai-hub',
      baseUrl: 'https://api.openai-hub.net/v1'
    }
  },
  metadata: {
    website: {
      apiKey: 'https://api.openai-hub.net/register?aff=kwxm',
      docs: 'https://docs.openai-hub.com/8692411m0',
      models: 'https://www.openai-hub.net',
      official: 'https://www.openai-hub.net'
    }
  }
})
