import type { ApiClient } from './client'
import type { TelegramConfig } from './types'

/** docs/API.md §6 */
export function telegramApi(c: ApiClient) {
  return {
    config: () => c.get<TelegramConfig>('/telegram/config'),
  }
}
