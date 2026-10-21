import { useState, type FormEvent } from 'react'
import { Navigate, useNavigate, useSearchParams } from 'react-router'
import { isApiError } from '../api/client'
import { useApi } from '../api/context'
import type { Me, OtpChallenge, TelegramWidgetUser } from '../api/types'
import { useAction } from '../hooks/useAction'
import { useNow } from '../hooks/useNow'
import { useQuery } from '../hooks/useQuery'
import { useTitle } from '../hooks/useTitle'
import { useI18n } from '../i18n/context'
import { isValidUzPhone, normalizePhone } from '../lib/phone'
import { Field, FormError, Notice, PasswordInput, PhoneInput, SubmitButton } from '../ui/forms'
import { LoadingState } from '../ui/states'
import { landingFor, safeNext, useSession } from './session'
import { TelegramLoginButton } from './TelegramLoginButton'

type Tab = 'patient' | 'staff'

export function LoginPage() {
  const { t } = useI18n()
  useTitle(t('login.title'))
  const { status, me, setMe } = useSession()
  const api = useApi()
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const next = safeNext(params.get('next'))
  const [tab, setTab] = useState<Tab>(params.get('mode') === 'staff' ? 'staff' : 'patient')
  const telegram = useQuery('telegram-config', () => api.telegram.config())
  const tgLogin = useAction((user: TelegramWidgetUser, totpCode?: string) => api.auth.telegramWidget(user, totpCode))
  const [tgPending, setTgPending] = useState<TelegramWidgetUser | null>(null)

  const done = (signedIn: Me) => {
    setMe(signedIn)
    navigate(next ?? landingFor(signedIn), { replace: true })
  }

  if (status === 'loading') return <LoadingState />
  if (status === 'authenticated' && me) return <Navigate replace to={next ?? landingFor(me)} />

  return (
    <div className="page narrow">
      <div className="card stack">
        <h1>{t('login.title')}</h1>
        <div className="tabs" role="tablist" aria-label={t('login.title')}>
          <button type="button" role="tab" id="tab-patient" aria-selected={tab === 'patient'} aria-controls="panel-patient" className="tabs__tab" onClick={() => setTab('patient')}>
            {t('login.patientTab')}
          </button>
          <button type="button" role="tab" id="tab-staff" aria-selected={tab === 'staff'} aria-controls="panel-staff" className="tabs__tab" onClick={() => setTab('staff')}>
            {t('login.staffTab')}
          </button>
        </div>

        {tab === 'patient' ? (
          <div role="tabpanel" id="panel-patient" aria-labelledby="tab-patient" className="stack">
            <OtpLogin onSignedIn={done} />
            {telegram.data?.enabled && telegram.data.botUsername && (
              <>
                <p className="divider">
                  <span>{t('login.or')}</span>
                </p>
                <TelegramLoginButton
                  config={telegram.data}
                  next={next}
                  onAuth={async (user) => {
                    const r = await tgLogin.run(user)
                    if (r.ok) done(r.value)
                    else if (isApiError(r.error, 'auth.totp_required')) setTgPending(user)
                  }}
                />
                {tgPending ? (
                  <TotpStep
                    pending={tgLogin.pending}
                    error={isApiError(tgLogin.error, 'auth.totp_required') ? undefined : tgLogin.error}
                    onSubmit={async (code) => {
                      const r = await tgLogin.run(tgPending, code)
                      if (r.ok) done(r.value)
                    }}
                  />
                ) : (
                  <FormError error={tgLogin.error} />
                )}
              </>
            )}
          </div>
        ) : (
          <div role="tabpanel" id="panel-staff" aria-labelledby="tab-staff" className="stack">
            <StaffLogin onSignedIn={done} />
          </div>
        )}
      </div>
    </div>
  )
}

/** Second factor prompt used after a sign-in method answers auth.totp_required. */
export function TotpStep({ pending, error, onSubmit }: { pending: boolean; error: unknown; onSubmit: (code: string) => void }) {
  const { t } = useI18n()
  const [code, setCode] = useState('')
  const [touched, setTouched] = useState(false)
  const invalid = !/^\d{6}$/.test(code) ? t('valid.code') : null
  return (
    <form
      className="stack"
      noValidate
      onSubmit={(e) => {
        e.preventDefault()
        setTouched(true)
        if (!invalid) onSubmit(code)
      }}
    >
      <Notice tone="info">{t('err.auth.totp_required')}</Notice>
      <Field label={t('login.totp')} hint={t('login.totpHint')} error={touched ? invalid : null}>
        {(p) => <input {...p} inputMode="numeric" autoComplete="one-time-code" maxLength={6} value={code} onChange={(e) => setCode(e.target.value.replace(/\D/g, ''))} />}
      </Field>
      <FormError error={error} />
      <SubmitButton pending={pending}>{t('login.verify')}</SubmitButton>
    </form>
  )
}

/** Phone → SMS code. Resend is offered after the server-provided cooldown. */
export function OtpLogin({ onSignedIn }: { onSignedIn: (me: Me) => void }) {
  const { t } = useI18n()
  const api = useApi()
  const [phone, setPhone] = useState('')
  const [code, setCode] = useState('')
  const [touched, setTouched] = useState(false)
  const [challenge, setChallenge] = useState<(OtpChallenge & { resendAt: number }) | null>(null)
  const [needTotp, setNeedTotp] = useState(false)
  const [totp, setTotp] = useState('')
  const request = useAction((p: string) => api.auth.requestOtp(p))
  const verify = useAction((id: string, c: string, second?: string) => api.auth.verifyOtp(id, c, second))
  const now = useNow(1000)

  const phoneError = !phone.trim() ? t('valid.required') : !isValidUzPhone(phone) ? t('valid.phone') : null
  const codeError = !/^\d{6}$/.test(code.trim()) ? t('valid.code') : null
  const totpError = needTotp && !/^\d{6}$/.test(totp) ? t('valid.code') : null

  async function send(e?: FormEvent) {
    e?.preventDefault()
    setTouched(true)
    if (phoneError) return
    const r = await request.run(normalizePhone(phone))
    if (r.ok) {
      setChallenge({ ...r.value, resendAt: Date.now() + r.value.resendAfterSeconds * 1000 })
      setCode('')
      setTouched(false)
      verify.clearError()
    }
  }

  async function submitCode(e: FormEvent) {
    e.preventDefault()
    setTouched(true)
    if (!challenge || codeError || totpError) return
    const r = await verify.run(challenge.challengeId, code.trim(), needTotp ? totp : undefined)
    if (r.ok) onSignedIn(r.value)
    else if (isApiError(r.error, 'auth.totp_required')) setNeedTotp(true)
  }

  if (!challenge) {
    return (
      <form onSubmit={send} noValidate className="stack">
        <Field label={t('login.phone')} hint={t('login.phoneHint')} error={touched ? phoneError : null}>
          {(p) => <PhoneInput {...p} autoComplete="tel" value={phone} onChange={setPhone} />}
        </Field>
        <FormError error={request.error} />
        <SubmitButton pending={request.pending}>{t('login.sendCode')}</SubmitButton>
      </form>
    )
  }

  const wait = Math.max(0, Math.ceil((challenge.resendAt - now) / 1000))
  return (
    <form onSubmit={submitCode} noValidate className="stack">
      <Notice tone="info">{t('login.codeSent')}</Notice>
      <Field label={t('login.code')} error={touched ? codeError : null}>
        {(p) => (
          <input
            {...p}
            inputMode="numeric"
            autoComplete="one-time-code"
            maxLength={6}
            value={code}
            onChange={(e) => setCode(e.target.value.replace(/\D/g, ''))}
          />
        )}
      </Field>
      {needTotp && (
        <Field label={t('login.totp')} hint={t('login.totpHint')} error={touched ? totpError : null}>
          {(p) => <input {...p} inputMode="numeric" autoComplete="one-time-code" maxLength={6} value={totp} onChange={(e) => setTotp(e.target.value.replace(/\D/g, ''))} />}
        </Field>
      )}
      <FormError error={isApiError(verify.error, 'auth.totp_required') ? undefined : (verify.error ?? request.error)} />
      <SubmitButton pending={verify.pending}>{t('login.verify')}</SubmitButton>
      <div className="row row--between">
        <button type="button" className="btn btn--ghost btn--small" onClick={() => setChallenge(null)}>
          {t('login.changePhone')}
        </button>
        <button type="button" className="btn btn--ghost btn--small" disabled={wait > 0 || request.pending} onClick={() => void send()}>
          {wait > 0 ? t('login.resendIn', { seconds: wait }) : t('login.resend')}
        </button>
      </div>
    </form>
  )
}

function StaffLogin({ onSignedIn }: { onSignedIn: (me: Me) => void }) {
  const { t } = useI18n()
  const api = useApi()
  const [mode, setMode] = useState<'login' | 'reset'>('login')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [totp, setTotp] = useState('')
  const [needTotp, setNeedTotp] = useState(false)
  const [touched, setTouched] = useState(false)
  const [resetDone, setResetDone] = useState(false)
  const login = useAction((body: { email: string; password: string; totpCode?: string }) => api.auth.passwordLogin(body))

  const emailError = !email.trim() ? t('valid.required') : !/^\S+@\S+\.\S+$/.test(email.trim()) ? t('valid.email') : null
  const passwordError = !password ? t('valid.required') : null
  const totpError = needTotp && !/^\d{6}$/.test(totp.trim()) ? t('valid.code') : null

  async function submit(e: FormEvent) {
    e.preventDefault()
    setTouched(true)
    if (emailError || passwordError || totpError) return
    const r = await login.run({ email: email.trim(), password, ...(needTotp ? { totpCode: totp.trim() } : {}) })
    if (r.ok) onSignedIn(r.value)
    else if (isApiError(r.error, 'auth.totp_required')) setNeedTotp(true)
  }

  if (mode === 'reset') {
    return (
      <PasswordReset
        initialEmail={email}
        onDone={() => {
          setMode('login')
          setResetDone(true)
        }}
        onCancel={() => setMode('login')}
      />
    )
  }

  // The TOTP prompt is not an error for the user, so it is shown as guidance, not as a red alert.
  const shownError = isApiError(login.error, 'auth.totp_required') ? undefined : login.error

  return (
    <form onSubmit={submit} noValidate className="stack">
      {resetDone && <Notice>{t('login.resetDone')}</Notice>}
      <Field label={t('login.email')} error={touched ? emailError : null}>
        {(p) => <input {...p} type="email" autoComplete="username" value={email} onChange={(e) => setEmail(e.target.value)} />}
      </Field>
      <Field label={t('login.password')} error={touched ? passwordError : null}>
        {(p) => <PasswordInput {...p} autoComplete="current-password" value={password} onChange={setPassword} />}
      </Field>
      {needTotp && (
        <Field label={t('login.totp')} hint={t('login.totpHint')} error={touched ? totpError : null}>
          {(p) => (
            <input
              {...p}
              inputMode="numeric"
              autoComplete="one-time-code"
              maxLength={6}
              autoFocus
              value={totp}
              onChange={(e) => setTotp(e.target.value.replace(/\D/g, ''))}
            />
          )}
        </Field>
      )}
      <FormError error={shownError} />
      <SubmitButton pending={login.pending}>{t('login.signIn')}</SubmitButton>
      <button type="button" className="btn btn--ghost btn--small" onClick={() => setMode('reset')}>
        {t('login.forgot')}
      </button>
      <p className="muted small">{t('login.totpNote')}</p>
    </form>
  )
}

function PasswordReset({ initialEmail, onDone, onCancel }: { initialEmail: string; onDone: () => void; onCancel: () => void }) {
  const { t } = useI18n()
  const api = useApi()
  const [email, setEmail] = useState(initialEmail)
  const [challengeId, setChallengeId] = useState<string | null>(null)
  const [requested, setRequested] = useState(false)
  const [code, setCode] = useState('')
  const [password, setPassword] = useState('')
  const [touched, setTouched] = useState(false)
  const request = useAction((e: string) => api.auth.requestPasswordReset(e))
  const confirm = useAction((body: { challengeId: string; code: string; newPassword: string }) => api.auth.confirmPasswordReset(body))

  const emailError = !/^\S+@\S+\.\S+$/.test(email.trim()) ? t('valid.email') : null
  const codeError = !/^\d{6}$/.test(code.trim()) ? t('valid.code') : null
  const passwordError = password.length < 10 ? t('valid.password') : null

  async function submitEmail(e: FormEvent) {
    e.preventDefault()
    setTouched(true)
    if (emailError) return
    const r = await request.run(email.trim())
    if (r.ok) {
      setRequested(true)
      setChallengeId(r.value?.challengeId ?? null)
      setTouched(false)
    }
  }

  async function submitCode(e: FormEvent) {
    e.preventDefault()
    setTouched(true)
    if (!challengeId || codeError || passwordError) return
    const r = await confirm.run({ challengeId, code: code.trim(), newPassword: password })
    if (r.ok) onDone()
  }

  if (!requested) {
    return (
      <form onSubmit={submitEmail} noValidate className="stack">
        <h2>{t('login.resetTitle')}</h2>
        <p className="muted">{t('login.resetIntro')}</p>
        <Field label={t('login.email')} error={touched ? emailError : null}>
          {(p) => <input {...p} type="email" autoComplete="username" value={email} onChange={(e) => setEmail(e.target.value)} />}
        </Field>
        <FormError error={request.error} />
        <div className="row">
          <SubmitButton pending={request.pending}>{t('login.sendCode')}</SubmitButton>
          <button type="button" className="btn btn--ghost" onClick={onCancel}>
            {t('common.back')}
          </button>
        </div>
      </form>
    )
  }

  return (
    <form onSubmit={submitCode} noValidate className="stack">
      <h2>{t('login.resetTitle')}</h2>
      <Notice tone="info">{t('login.resetSent')}</Notice>
      {challengeId && (
        <>
          <Field label={t('login.code')} error={touched ? codeError : null}>
            {(p) => <input {...p} inputMode="numeric" autoComplete="one-time-code" maxLength={6} value={code} onChange={(e) => setCode(e.target.value.replace(/\D/g, ''))} />}
          </Field>
          <Field label={t('login.newPassword')} error={touched ? passwordError : null}>
            {(p) => <PasswordInput {...p} autoComplete="new-password" value={password} onChange={setPassword} />}
          </Field>
          <FormError error={confirm.error} />
          <SubmitButton pending={confirm.pending}>{t('login.resetSubmit')}</SubmitButton>
        </>
      )}
      <button type="button" className="btn btn--ghost btn--small" onClick={onCancel}>
        {t('common.back')}
      </button>
    </form>
  )
}
