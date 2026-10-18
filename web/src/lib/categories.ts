import { serviceCategories, type ServiceCategory } from '../api/types'
import type { MessageKey } from '../i18n/messages'

type Translate = (key: MessageKey, params?: Record<string, string | number>) => string

export function isServiceCategory(key: string): key is ServiceCategory {
  return (serviceCategories as readonly string[]).includes(key)
}

/**
 * Localised name of a fixed service category key (API.md §2.1). An unknown key (a newer server
 * catalogue) falls back to the server-provided Uzbek name, then to the key itself.
 */
export function categoryLabel(t: Translate, key: string, fallback?: string): string {
  return isServiceCategory(key) ? t(`category.${key}`) : (fallback ?? key)
}
