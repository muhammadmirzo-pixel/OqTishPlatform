import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { useApi } from '../api/context'
import { useI18n } from '../i18n/context'
import { FormError } from '../ui/forms'
import { LoadingState } from '../ui/states'
import { landingFor, safeNext, useSession } from './session'
import { widgetUserFromParams } from './telegramWidget'

/**
 * Target of the Login Widget redirect mode (/login/telegram?id=…&hash=…). The signed payload is
 * removed from the address bar immediately, then verified by the server.
 */
export function TelegramCallbackPage() {
  const { t } = useI18n()
  const api = useApi()
  const { setMe } = useSession()
  const navigate = useNavigate()
  // Read the payload once, on first render, before the address bar is cleaned.
  const [payload] = useState(() => {
    const params = new URLSearchParams(window.location.search)
    return { next: safeNext(params.get('next')), user: widgetUserFromParams(params) }
  })
  const [error, setError] = useState<unknown>(payload.user ? undefined : new Error('invalid'))
  const started = useRef(false)

  useEffect(() => {
    window.history.replaceState(null, '', '/login/telegram')
    const { user, next } = payload
    if (started.current || !user) return
    started.current = true
    api.auth.telegramWidget(user).then(
      (me) => {
        setMe(me)
        navigate(next ?? landingFor(me), { replace: true })
      },
      (err: unknown) => setError(err),
    )
  }, [api, navigate, setMe, payload])

  if (error === undefined) return <LoadingState />
  return (
    <div className="page narrow">
      <div className="card stack">
        <h1>{t('login.title')}</h1>
        <FormError error={error} />
        <Link className="btn" to="/login">
          {t('login.backToLogin')}
        </Link>
      </div>
    </div>
  )
}
