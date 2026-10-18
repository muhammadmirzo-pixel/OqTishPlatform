import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router'
import { useApi } from '../../api/context'
import type { TelegramLink } from '../../api/types'
import { useSession } from '../../auth/session'
import { useAction } from '../../hooks/useAction'
import { useNow } from '../../hooks/useNow'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { remaining } from '../../lib/format'
import { isValidUzPhone, normalizePhone } from '../../lib/phone'
import { Checkbox, Field, FormError, Notice, SubmitButton } from '../../ui/forms'
import { ConfirmDialog } from '../../ui/ReasonDialog'
import { Badge } from '../../ui/widgets'

export function ProfilePage() {
  const { t } = useI18n()
  useTitle(t('account.title'))
  return (
    <div className="stack">
      <NameCard />
      <PhoneCard />
      <TelegramCard />
      <DeleteCard />
    </div>
  )
}

function NameCard() {
  const { t } = useI18n()
  const api = useApi()
  const { me, refresh } = useSession()
  const [name, setName] = useState(me?.patient?.fullName ?? me?.displayName ?? '')
  const [touched, setTouched] = useState(false)
  const [saved, setSaved] = useState(false)
  const save = useAction((fullName: string) => api.me.updateProfile(fullName))
  const error = !name.trim() ? t('valid.required') : name.trim().length > 100 ? t('valid.maxLength', { max: 100 }) : null

  async function submit(e: FormEvent) {
    e.preventDefault()
    setTouched(true)
    setSaved(false)
    if (error) return
    const r = await save.run(name.trim())
    if (r.ok) {
      setSaved(true)
      await refresh()
    }
  }

  return (
    <section className="card stack" aria-labelledby="name-title">
      <h2 id="name-title">{t('account.profile')}</h2>
      <form className="stack" onSubmit={submit} noValidate>
        <Field label={t('account.fullName')} hint={t('account.fullNameHint')} error={touched ? error : null}>
          {(p) => <input {...p} autoComplete="name" maxLength={100} value={name} onChange={(e) => setName(e.target.value)} />}
        </Field>
        {me?.email && <p className="muted small">{t('account.email', { email: me.email })}</p>}
        <FormError error={save.error} />
        {saved && <Notice>{t('common.saved')}</Notice>}
        <SubmitButton pending={save.pending}>{t('common.save')}</SubmitButton>
      </form>
    </section>
  )
}

function PhoneCard() {
  const { t } = useI18n()
  const api = useApi()
  const { me, refresh } = useSession()
  const [phone, setPhone] = useState('')
  const [code, setCode] = useState('')
  const [challengeId, setChallengeId] = useState<string | null>(null)
  const [touched, setTouched] = useState(false)
  const [done, setDone] = useState(false)
  const request = useAction((p: string) => api.me.requestPhone(p))
  const verify = useAction((id: string, c: string) => api.me.verifyPhone(id, c))
  const phoneError = !isValidUzPhone(phone) ? t('valid.phone') : null
  const codeError = !/^\d{6}$/.test(code) ? t('valid.code') : null

  async function submitPhone(e: FormEvent) {
    e.preventDefault()
    setTouched(true)
    if (phoneError) return
    const r = await request.run(normalizePhone(phone))
    if (r.ok) {
      setChallengeId(r.value.challengeId)
      setTouched(false)
    }
  }

  async function submitCode(e: FormEvent) {
    e.preventDefault()
    setTouched(true)
    if (!challengeId || codeError) return
    const r = await verify.run(challengeId, code)
    if (r.ok) {
      setDone(true)
      setChallengeId(null)
      setPhone('')
      setCode('')
      await refresh()
    }
  }

  return (
    <section className="card stack" aria-labelledby="phone-title">
      <h2 id="phone-title">{t('account.phone')}</h2>
      <p>
        {me?.phoneMasked ? (
          <>
            {me.phoneMasked} {me.phoneVerified ? <Badge tone="success">{t('account.verified')}</Badge> : <Badge tone="warning">{t('account.unverified')}</Badge>}
          </>
        ) : (
          <span className="muted">{t('account.noPhone')}</span>
        )}
      </p>
      {done && <Notice>{t('account.phoneLinked')}</Notice>}
      {!challengeId ? (
        <form className="stack" onSubmit={submitPhone} noValidate>
          <Field label={me?.phoneMasked ? t('account.newPhone') : t('login.phone')} hint={t('account.phoneHint')} error={touched ? phoneError : null}>
            {(p) => <input {...p} type="tel" inputMode="tel" autoComplete="tel" placeholder="+998 90 123 45 67" value={phone} onChange={(e) => setPhone(e.target.value)} />}
          </Field>
          <FormError error={request.error} />
          <SubmitButton pending={request.pending}>{t('login.sendCode')}</SubmitButton>
        </form>
      ) : (
        <form className="stack" onSubmit={submitCode} noValidate>
          <Notice tone="info">{t('login.codeSent')}</Notice>
          <Field label={t('login.code')} error={touched ? codeError : null}>
            {(p) => <input {...p} inputMode="numeric" autoComplete="one-time-code" maxLength={6} value={code} onChange={(e) => setCode(e.target.value.replace(/\D/g, ''))} />}
          </Field>
          <FormError error={verify.error} />
          <div className="row">
            <SubmitButton pending={verify.pending}>{t('login.verify')}</SubmitButton>
            <button type="button" className="btn btn--ghost" onClick={() => setChallengeId(null)}>
              {t('common.cancel')}
            </button>
          </div>
        </form>
      )}
    </section>
  )
}

function TelegramCard() {
  const { t } = useI18n()
  const api = useApi()
  const { me, refresh } = useSession()
  const [link, setLink] = useState<TelegramLink | null>(null)
  const [unlinking, setUnlinking] = useState(false)
  const create = useAction(() => api.me.telegramLink())
  const now = useNow(1000)
  const left = link ? remaining(link.expiresAt, now) : null

  return (
    <section className="card stack" aria-labelledby="tg-title">
      <h2 id="tg-title">{t('account.telegram')}</h2>
      {me?.telegramLinked ? (
        <>
          <p>
            <Badge tone="success">{t('account.telegramLinked')}</Badge>
          </p>
          <p className="muted small">{t('account.telegramLinkedNote')}</p>
          <div>
            <button type="button" className="btn btn--ghost" onClick={() => setUnlinking(true)}>
              {t('account.telegramUnlink')}
            </button>
          </div>
        </>
      ) : (
        <>
          <p className="muted">{t('account.telegramIntro')}</p>
          {link && left ? (
            <div className="stack">
              <a className="btn btn--telegram" href={link.deepLink} target="_blank" rel="noopener noreferrer">
                {t('account.telegramOpen')}
              </a>
              <p className="muted small">{t('account.telegramExpires', { time: left })}</p>
              <button type="button" className="btn btn--ghost" onClick={() => void refresh()}>
                {t('account.telegramDone')}
              </button>
            </div>
          ) : (
            <div>
              <button
                type="button"
                className="btn"
                disabled={create.pending}
                onClick={async () => {
                  const r = await create.run()
                  if (r.ok) setLink(r.value)
                }}
              >
                {link ? t('account.telegramNewLink') : t('account.telegramLink')}
              </button>
            </div>
          )}
          <FormError error={create.error} />
        </>
      )}
      {unlinking && (
        <ConfirmDialog
          title={t('account.telegramUnlink')}
          body={<p>{t('account.telegramUnlinkBody')}</p>}
          confirmLabel={t('account.telegramUnlink')}
          danger
          onConfirm={() => api.me.telegramUnlink()}
          onDone={() => {
            setUnlinking(false)
            setLink(null)
            void refresh()
          }}
          onClose={() => setUnlinking(false)}
        />
      )}
    </section>
  )
}

function DeleteCard() {
  const { t } = useI18n()
  const api = useApi()
  const navigate = useNavigate()
  const { me, setMe } = useSession()
  const [open, setOpen] = useState(false)
  const [understood, setUnderstood] = useState(false)
  const isStaff = (me?.memberships.length ?? 0) > 0

  return (
    <section className="card card--danger stack" aria-labelledby="delete-title">
      <h2 id="delete-title">{t('account.delete')}</h2>
      <p className="muted">{t('account.deleteIntro')}</p>
      {isStaff && <p className="alert alert--warning">{t('account.deleteStaff')}</p>}
      <div>
        <button type="button" className="btn btn--danger" disabled={isStaff} onClick={() => setOpen(true)}>
          {t('account.delete')}
        </button>
      </div>
      {open && (
        <ConfirmDialog
          title={t('account.deleteTitle')}
          body={
            <div className="stack">
              <p>{t('account.deleteBody')}</p>
              <Checkbox label={t('account.deleteUnderstand')} checked={understood} onChange={setUnderstood} />
            </div>
          }
          confirmLabel={t('account.deleteYes')}
          danger
          confirmDisabled={!understood}
          onConfirm={() => api.me.deleteAccount()}
          onDone={() => {
            setOpen(false)
            setMe(null)
            navigate('/', { replace: true })
          }}
          onClose={() => setOpen(false)}
        />
      )}
    </section>
  )
}
