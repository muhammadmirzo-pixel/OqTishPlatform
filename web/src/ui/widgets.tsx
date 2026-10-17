import type { ReactNode } from 'react'
import type { AppointmentStatus } from '../domain/types'
import { useI18n } from '../i18n/context'
import { localeNames, locales, type Locale } from '../i18n/messages'
import { remaining } from '../lib/format'
import { useNow } from '../hooks/useNow'

const statusTone: Record<AppointmentStatus, string> = {
  Pending: 'warning',
  Confirmed: 'info',
  Completed: 'success',
  Rejected: 'neutral',
  CancelledByPatient: 'neutral',
  CancelledByClinic: 'neutral',
  Expired: 'neutral',
  NoShow: 'danger',
}

export function StatusBadge({ status }: { status: AppointmentStatus }) {
  const { t } = useI18n()
  return <span className={`badge badge--${statusTone[status]}`}>{t(`status.${status}`)}</span>
}

export function Badge({ tone = 'neutral', children }: { tone?: string; children: ReactNode }) {
  return <span className={`badge badge--${tone}`}>{children}</span>
}

/**
 * Shows time left until a server deadline. Purely informational: the server decides expiry, so
 * when the clock reaches zero we show `expiredText` but never change the status ourselves.
 */
export function Countdown({ deadline, render, expiredText }: { deadline: string; render: (time: string) => string; expiredText: string }) {
  const now = useNow()
  const left = remaining(deadline, now)
  return (
    <span className={left ? 'countdown' : 'countdown countdown--over'} aria-live="off">
      {left ? render(left) : expiredText}
    </span>
  )
}

export function LanguageSwitcher() {
  const { locale, setLocale, t } = useI18n()
  return (
    <label className="lang">
      <span className="visually-hidden">{t('lang.label')}</span>
      <select value={locale} onChange={(e) => setLocale(e.target.value as Locale)} aria-label={t('lang.label')}>
        {locales.map((l) => (
          <option key={l} value={l}>
            {localeNames[l]}
          </option>
        ))}
      </select>
    </label>
  )
}

export function PageHeader({ title, subtitle, actions }: { title: string; subtitle?: string; actions?: ReactNode }) {
  return (
    <header className="page-header">
      <div>
        <h1>{title}</h1>
        {subtitle && <p className="muted">{subtitle}</p>}
      </div>
      {actions && <div className="page-header__actions">{actions}</div>}
    </header>
  )
}

export function StatCard({ label, value, tone }: { label: string; value: ReactNode; tone?: string }) {
  return (
    <div className={`stat${tone ? ` stat--${tone}` : ''}`}>
      <span className="stat__label">{label}</span>
      <span className="stat__value">{value}</span>
    </div>
  )
}
