import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router'
import { useApi } from '../../api/context'
import type { TotpSetup } from '../../api/types'
import { useSession } from '../../auth/session'
import { useAction } from '../../hooks/useAction'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { formatFullDateTime } from '../../lib/format'
import { DEFAULT_TIME_ZONE } from '../../lib/time'
import { Field, FormError, Notice, PasswordInput, SubmitButton } from '../../ui/forms'
import { ConfirmDialog } from '../../ui/ReasonDialog'
import { ApiErrorState, LoadingState } from '../../ui/states'
import { Badge, CopyButton } from '../../ui/widgets'

export function SecurityPage() {
  const { t } = useI18n()
  useTitle(t('account.tabSecurity'))
  return (
    <div className="stack">
      <TotpCard />
      <PasswordCard />
      <SessionsCard />
    </div>
  )
}

function TotpCard() {
  const { t } = useI18n()
  const api = useApi()
  const { me, refresh } = useSession()
  const [setup, setSetup] = useState<TotpSetup | null>(null)
  const [code, setCode] = useState('')
  const [touched, setTouched] = useState(false)
  const [message, setMessage] = useState<'enabled' | 'disabled' | null>(null)
  const start = useAction(() => api.me.totpSetup())
  const enable = useAction((c: string) => api.me.totpEnable(c))
  const disable = useAction((c: string) => api.me.totpDisable(c))
  const codeError = !/^\d{6}$/.test(code) ? t('valid.code') : null

  async function submitEnable(e: FormEvent) {
    e.preventDefault()
    setTouched(true)
    if (codeError) return
    const r = await enable.run(code)
    if (r.ok) {
      setSetup(null)
      setCode('')
      setTouched(false)
      setMessage('enabled')
      await refresh()
    }
  }

  async function submitDisable(e: FormEvent) {
    e.preventDefault()
    setTouched(true)
    if (codeError) return
    const r = await disable.run(code)
    if (r.ok) {
      setCode('')
      setTouched(false)
      setMessage('disabled')
      await refresh()
    }
  }

  const codeField = (
    <Field label={t('login.totp')} hint={t('login.totpHint')} error={touched ? codeError : null}>
      {(p) => <input {...p} inputMode="numeric" autoComplete="one-time-code" maxLength={6} value={code} onChange={(e) => setCode(e.target.value.replace(/\D/g, ''))} />}
    </Field>
  )

  return (
    <section className="card stack" aria-labelledby="totp-title">
      <h2 id="totp-title">
        {t('totp.title')} {me?.totpEnabled ? <Badge tone="success">{t('totp.on')}</Badge> : <Badge tone="warning">{t('totp.off')}</Badge>}
      </h2>
      <p className="muted">{t('totp.intro')}</p>
      {message && <Notice>{t(message === 'enabled' ? 'totp.enabledNotice' : 'totp.disabledNotice')}</Notice>}
      {me?.totpEnabled ? (
        <form className="stack" onSubmit={submitDisable} noValidate>
          <p className="small">{t('totp.disableIntro')}</p>
          {codeField}
          <FormError error={disable.error} />
          <SubmitButton pending={disable.pending} className="btn btn--danger">
            {t('totp.disable')}
          </SubmitButton>
        </form>
      ) : setup ? (
        <form className="stack" onSubmit={submitEnable} noValidate>
          <ol className="notes">
            <li>{t('totp.step1')}</li>
            <li>{t('totp.step2')}</li>
          </ol>
          <div className="secret">
            <span className="muted small">{t('totp.secret')}</span>
            <code className="secret__value">{setup.secret}</code>
            <CopyButton text={setup.secret} />
          </div>
          <details>
            <summary>{t('totp.uri')}</summary>
            <code className="secret__value secret__value--wrap">{setup.otpauthUri}</code>
            <CopyButton text={setup.otpauthUri} />
          </details>
          {codeField}
          <FormError error={enable.error} />
          <div className="row">
            <SubmitButton pending={enable.pending}>{t('totp.enable')}</SubmitButton>
            <button type="button" className="btn btn--ghost" onClick={() => setSetup(null)}>
              {t('common.cancel')}
            </button>
          </div>
        </form>
      ) : (
        <div className="stack">
          <FormError error={start.error} />
          <div>
            <button
              type="button"
              className="btn"
              disabled={start.pending}
              onClick={async () => {
                setMessage(null)
                const r = await start.run()
                if (r.ok) setSetup(r.value)
              }}
            >
              {t('totp.start')}
            </button>
          </div>
        </div>
      )}
    </section>
  )
}

function PasswordCard() {
  const { t } = useI18n()
  const api = useApi()
  const { me, refresh } = useSession()
  const firstPassword = !me?.email
  const [email, setEmail] = useState('')
  const [current, setCurrent] = useState('')
  const [next, setNext] = useState('')
  const [repeat, setRepeat] = useState('')
  const [touched, setTouched] = useState(false)
  const [done, setDone] = useState(false)
  const save = useAction((body: { email?: string; currentPassword?: string; newPassword: string }) => api.me.setPassword(body))
  const emailError = firstPassword && !/^\S+@\S+\.\S+$/.test(email.trim()) ? t('valid.email') : null
  const currentError = !firstPassword && !current ? t('valid.required') : null
  const nextError = next.length < 10 ? t('valid.password') : null
  const repeatError = repeat !== next ? t('valid.passwordMatch') : null

  async function submit(e: FormEvent) {
    e.preventDefault()
    setTouched(true)
    setDone(false)
    if (nextError || repeatError || emailError || currentError) return
    const r = await save.run(firstPassword ? { email: email.trim(), newPassword: next } : { currentPassword: current, newPassword: next })
    if (r.ok) {
      setDone(true)
      if (firstPassword) void refresh()
      setCurrent('')
      setNext('')
      setRepeat('')
      setTouched(false)
    }
  }

  return (
    <section className="card stack" aria-labelledby="password-title">
      <h2 id="password-title">{t('password.title')}</h2>
      <p className="muted">{t('password.intro')}</p>
      <form className="stack" onSubmit={submit} noValidate>
        {firstPassword ? (
          <Field label={t('login.email')} hint={t('password.emailHint')} error={touched ? emailError : null}>
            {(p) => <input {...p} type="email" autoComplete="username" value={email} onChange={(e) => setEmail(e.target.value)} />}
          </Field>
        ) : (
          <Field label={t('password.current')} error={touched ? currentError : null}>
            {(p) => <PasswordInput {...p} autoComplete="current-password" value={current} onChange={setCurrent} />}
          </Field>
        )}
        <Field label={t('login.newPassword')} error={touched ? nextError : null}>
          {(p) => <PasswordInput {...p} autoComplete="new-password" value={next} onChange={setNext} />}
        </Field>
        <Field label={t('password.repeat')} error={touched ? repeatError : null}>
          {(p) => <PasswordInput {...p} autoComplete="new-password" value={repeat} onChange={setRepeat} />}
        </Field>
        <FormError error={save.error} />
        {done && <Notice>{t('password.done')}</Notice>}
        <SubmitButton pending={save.pending}>{t('password.save')}</SubmitButton>
      </form>
    </section>
  )
}

function SessionsCard() {
  const { t, locale } = useI18n()
  const api = useApi()
  const navigate = useNavigate()
  const { signOut } = useSession()
  const sessions = useQuery('sessions', () => api.me.sessions())
  const [revoking, setRevoking] = useState<string | null>(null)
  const [all, setAll] = useState(false)

  return (
    <section className="card stack" aria-labelledby="sessions-title">
      <h2 id="sessions-title">{t('sessions.title')}</h2>
      {sessions.error !== undefined ? (
        <ApiErrorState error={sessions.error} onRetry={sessions.reload} />
      ) : !sessions.data ? (
        <LoadingState inline />
      ) : (
        <ul className="list-plain stack">
          {sessions.data.map((s) => (
            <li key={s.id} className="row row--between session">
              <div>
                <strong>{s.userAgent ?? t('sessions.unknownDevice')}</strong> {s.current && <Badge tone="info">{t('sessions.current')}</Badge>}
                <div className="small">{t(`sessions.method.${s.authMethod}`)}</div>
                <div className="muted small">
                  {t('sessions.started', { time: formatFullDateTime(s.createdAt, DEFAULT_TIME_ZONE, locale) })} · {t('sessions.lastSeen', { time: formatFullDateTime(s.lastSeenAt, DEFAULT_TIME_ZONE, locale) })}
                </div>
              </div>
              {!s.current && (
                <button type="button" className="btn btn--ghost btn--small" onClick={() => setRevoking(s.id)}>
                  {t('sessions.revoke')}
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
      <div>
        <button type="button" className="btn btn--danger" onClick={() => setAll(true)}>
          {t('sessions.logoutAll')}
        </button>
      </div>
      {revoking && (
        <ConfirmDialog
          title={t('sessions.revoke')}
          body={<p>{t('sessions.revokeBody')}</p>}
          confirmLabel={t('sessions.revoke')}
          onConfirm={() => api.me.revokeSession(revoking)}
          onDone={() => {
            setRevoking(null)
            sessions.reload()
          }}
          onClose={() => setRevoking(null)}
        />
      )}
      {all && (
        <ConfirmDialog
          title={t('sessions.logoutAll')}
          body={<p>{t('sessions.logoutAllBody')}</p>}
          confirmLabel={t('sessions.logoutAll')}
          danger
          onConfirm={() => signOut(true)}
          onDone={() => navigate('/login', { replace: true })}
          onClose={() => setAll(false)}
        />
      )}
    </section>
  )
}
