import { useEffect, useRef, useState } from 'react'
import type { TelegramConfig, TelegramWidgetUser } from '../api/types'
import { useI18n } from '../i18n/context'
import { loadScript } from '../lib/script'

const WIDGET_SRC = 'https://telegram.org/js/telegram-widget.js?22'

import '../telegram/webapp'

/**
 * Telegram sign-in for the website. The widget script is loaded only when the backend reports
 * Telegram as enabled. Preferred mode: our own button calling Telegram.Login.auth (popup +
 * postMessage, no eval, needs `botId`). Fallback: the official widget in redirect mode
 * (data-auth-url → /login/telegram). The widget's `data-onauth` mode is avoided because it needs
 * eval, which the CSP forbids.
 */
export function TelegramLoginButton({ config, next, onAuth }: { config: TelegramConfig; next: string | null; onAuth: (user: TelegramWidgetUser) => void }) {
  const { t, locale } = useI18n()
  const host = useRef<HTMLDivElement>(null)
  const [ready, setReady] = useState(false)
  const [failed, setFailed] = useState(false)
  const botId = config.botId ?? null

  useEffect(() => {
    if (botId === null) return
    let alive = true
    loadScript(WIDGET_SRC).then(
      () => alive && setReady(Boolean(window.Telegram?.Login)),
      () => alive && setFailed(true),
    )
    return () => {
      alive = false
    }
  }, [botId])

  useEffect(() => {
    const container = host.current
    if (botId !== null || !container || !config.botUsername) return
    const script = document.createElement('script')
    script.src = WIDGET_SRC
    script.async = true
    const callback = new URL('/login/telegram', window.location.origin)
    if (next) callback.searchParams.set('next', next)
    script.setAttribute('data-telegram-login', config.botUsername)
    script.setAttribute('data-size', 'large')
    script.setAttribute('data-radius', '8')
    script.setAttribute('data-lang', locale)
    script.setAttribute('data-request-access', 'write')
    script.setAttribute('data-auth-url', callback.toString())
    script.onerror = () => setFailed(true)
    container.replaceChildren(script)
    return () => container.replaceChildren()
  }, [botId, config.botUsername, locale, next])

  if (botId !== null) {
    return (
      <div className="telegram-login">
        <button
          type="button"
          className="btn btn--telegram"
          disabled={!ready}
          onClick={() =>
            window.Telegram?.Login?.auth({ bot_id: botId, request_access: 'write', lang: locale }, (user) => {
              if (user) onAuth(user)
            })
          }
        >
          {t('login.telegram')}
        </button>
        {failed && <p className="muted small">{t('login.telegramUnavailable')}</p>}
      </div>
    )
  }

  return (
    <div className="telegram-login">
      <div ref={host} />
      {failed && <p className="muted small">{t('login.telegramUnavailable')}</p>}
    </div>
  )
}
