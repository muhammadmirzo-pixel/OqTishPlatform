// Telegram Mini App integration (https://core.telegram.org/bots/webapps). The SDK script is
// loaded only on /tg routes. initData is a credential: it is sent to the API once and never logged
// or stored.

import { loadScript } from '../lib/script'

export const WEBAPP_SRC = 'https://telegram.org/js/telegram-web-app.js'

export interface TelegramButton {
  setText(text: string): void
  show(): void
  hide(): void
  enable(): void
  disable(): void
  showProgress(leaveActive?: boolean): void
  hideProgress(): void
  onClick(cb: () => void): void
  offClick(cb: () => void): void
}

export interface TelegramWebApp {
  initData: string
  initDataUnsafe: { user?: { id: number; first_name?: string; language_code?: string } }
  colorScheme?: 'light' | 'dark'
  themeParams: Partial<Record<'bg_color' | 'text_color' | 'hint_color' | 'link_color' | 'button_color' | 'button_text_color' | 'secondary_bg_color' | 'destructive_text_color', string>>
  ready(): void
  expand(): void
  onEvent?(event: string, cb: () => void): void
  offEvent?(event: string, cb: () => void): void
  openLink?(url: string): void
  BackButton?: Pick<TelegramButton, 'show' | 'hide' | 'onClick' | 'offClick'>
  MainButton?: TelegramButton
}

declare global {
  interface Window {
    Telegram?: { WebApp?: TelegramWebApp; Login?: { auth: (options: { bot_id: number; request_access?: string; lang?: string }, callback: (user: import('../api/types').TelegramWidgetUser | false) => void) => void } }
  }
}

/** Resolves with the WebApp object (possibly without initData outside Telegram), or null if the SDK failed to load. */
export async function ensureWebApp(): Promise<TelegramWebApp | null> {
  if (window.Telegram?.WebApp) return window.Telegram.WebApp
  try {
    await loadScript(WEBAPP_SRC)
  } catch {
    return null
  }
  return window.Telegram?.WebApp ?? null
}

const themeMap: [keyof TelegramWebApp['themeParams'], string][] = [
  ['bg_color', '--bg'],
  ['secondary_bg_color', '--surface'],
  ['text_color', '--text'],
  ['hint_color', '--muted'],
  ['link_color', '--link'],
  ['button_color', '--accent'],
  ['button_text_color', '--accent-text'],
  ['destructive_text_color', '--danger'],
]

/** Copies Telegram theme colours into our CSS variables; returns a cleanup function. */
export function applyTheme(app: TelegramWebApp): () => void {
  const root = document.documentElement
  const set = () => {
    for (const [param, cssVar] of themeMap) {
      const value = app.themeParams[param]
      if (value && /^#[0-9a-f]{3,8}$/i.test(value)) root.style.setProperty(cssVar, value)
    }
    root.dataset.tgScheme = app.colorScheme ?? 'light'
  }
  set()
  app.onEvent?.('themeChanged', set)
  return () => {
    app.offEvent?.('themeChanged', set)
    for (const [, cssVar] of themeMap) root.style.removeProperty(cssVar)
    delete root.dataset.tgScheme
  }
}

export function telegramLocale(app: TelegramWebApp | null): 'uz' | 'ru' | 'en' | null {
  const code = app?.initDataUnsafe.user?.language_code?.slice(0, 2).toLowerCase()
  return code === 'uz' || code === 'ru' || code === 'en' ? code : null
}
