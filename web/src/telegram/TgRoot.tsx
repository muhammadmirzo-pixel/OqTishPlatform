import { useEffect, useRef, useState } from 'react'
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router'
import { isApiError } from '../api/client'
import { useApi } from '../api/context'
import { TotpStep } from '../auth/LoginPage'
import { useSession } from '../auth/session'
import { useI18n } from '../i18n/context'
import { FormError } from '../ui/forms'
import { LoadingState } from '../ui/states'
import { LanguageSwitcher } from '../ui/widgets'
import { TgContext } from './tgContext'
import { applyTheme, ensureWebApp, telegramLocale, type TelegramWebApp } from './webapp'

type Phase = { kind: 'loading' } | { kind: 'outside' } | { kind: 'error'; error: unknown } | { kind: 'totp'; app: TelegramWebApp; error?: unknown } | { kind: 'ready'; app: TelegramWebApp }

const rootPaths = ['/tg', '/tg/appointments', '/tg/staff']

/**
 * Mini App shell: loads the Telegram SDK (only here), signs in with initData, applies the
 * Telegram theme and language, and wires the native BackButton.
 */
export function TgRoot() {
  const api = useApi()
  const { t, setLocale, hasUserChoice } = useI18n()
  const { setMe } = useSession()
  const [phase, setPhase] = useState<Phase>({ kind: 'loading' })
  const started = useRef(false)

  useEffect(() => {
    api.client.setClientKind('tg-miniapp')
    document.documentElement.classList.add('tg')
    return () => document.documentElement.classList.remove('tg')
  }, [api])

  useEffect(() => {
    if (started.current) return
    started.current = true
    void (async () => {
      const app = await ensureWebApp()
      if (!app || !app.initData) {
        setPhase({ kind: 'outside' })
        return
      }
      app.ready()
      app.expand()
      const lang = telegramLocale(app)
      if (lang && !hasUserChoice()) setLocale(lang, { persist: false })
      try {
        setMe(await api.auth.telegramWebApp(app.initData))
        setPhase({ kind: 'ready', app })
      } catch (error) {
        setPhase(isApiError(error, 'auth.totp_required') ? { kind: 'totp', app } : { kind: 'error', error })
      }
    })()
  }, [api, setMe, setLocale, hasUserChoice])

  const readyApp = phase.kind === 'ready' ? phase.app : null
  useEffect(() => (readyApp ? applyTheme(readyApp) : undefined), [readyApp])

  if (phase.kind === 'loading') return <LoadingState />
  if (phase.kind === 'outside') {
    return (
      <main className="tg-page stack">
        <h1>{t('app.name')}</h1>
        <div className="alert alert--info" role="alert">
          {t('tg.notInTelegram')}
        </div>
        <a className="btn" href="/login">
          {t('tg.openWebLogin')}
        </a>
        <LanguageSwitcher />
      </main>
    )
  }
  if (phase.kind === 'totp') {
    const app = phase.app
    return (
      <main className="tg-page stack">
        <h1>{t('app.name')}</h1>
        <TotpStep
          pending={false}
          error={phase.error}
          onSubmit={async (code) => {
            try {
              setMe(await api.auth.telegramWebApp(app.initData, code))
              setPhase({ kind: 'ready', app })
            } catch (error) {
              setPhase({ kind: 'totp', app, error })
            }
          }}
        />
      </main>
    )
  }
  if (phase.kind === 'error') {
    return (
      <main className="tg-page stack">
        <h1>{t('app.name')}</h1>
        <FormError error={phase.error} />
        <button type="button" className="btn" onClick={() => window.location.reload()}>
          {t('common.retry')}
        </button>
      </main>
    )
  }
  return (
    <TgContext.Provider value={phase.app}>
      <TgShell app={phase.app} />
    </TgContext.Provider>
  )
}

function TgShell({ app }: { app: TelegramWebApp }) {
  const { t } = useI18n()
  const { me } = useSession()
  const location = useLocation()
  const navigate = useNavigate()
  const isRoot = rootPaths.includes(location.pathname.replace(/\/+$/, ''))
  const isStaff = (me?.memberships.length ?? 0) > 0

  useEffect(() => {
    const back = app.BackButton
    if (!back) return
    const onBack = () => navigate(-1)
    if (isRoot) back.hide()
    else {
      back.show()
      back.onClick(onBack)
    }
    return () => back.offClick(onBack)
  }, [app, isRoot, navigate])

  return (
    <div className="tg-app">
      <main id="main" className="tg-page">
        {!isRoot && !app.BackButton && (
          <button type="button" className="btn btn--ghost btn--small" onClick={() => navigate(-1)}>
            ← {t('common.back')}
          </button>
        )}
        <Outlet />
      </main>
      <nav className="tg-tabs" aria-label={t('nav.menu')}>
        <NavLink to="/tg" end>
          {t('tg.tabClinics')}
        </NavLink>
        <NavLink to="/tg/appointments">{t('tg.tabMine')}</NavLink>
        {isStaff && <NavLink to="/tg/staff">{t('tg.tabStaff')}</NavLink>}
      </nav>
    </div>
  )
}
