import { CalendarCheck, CalendarX, CheckCircle2, Hourglass, Star, TimerOff, UserX, XCircle, type LucideIcon } from 'lucide-react'
import { useState, type ReactNode } from 'react'
import type { AppointmentStatus } from '../api/types'
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

const statusIcon: Record<AppointmentStatus, LucideIcon> = {
  Pending: Hourglass,
  Confirmed: CalendarCheck,
  Completed: CheckCircle2,
  Rejected: XCircle,
  CancelledByPatient: CalendarX,
  CancelledByClinic: CalendarX,
  Expired: TimerOff,
  NoShow: UserX,
}

export function StatusIcon({ status, className = 'icon' }: { status: AppointmentStatus; className?: string }) {
  const Icon = statusIcon[status]
  return <Icon className={className} aria-hidden="true" />
}

/** Status pill: colour + icon + text, so status never depends on colour alone. */
export function StatusBadge({ status }: { status: AppointmentStatus }) {
  const { t } = useI18n()
  return (
    <span className={`badge badge--${statusTone[status]}`}>
      <StatusIcon status={status} />
      {t(`status.${status}`)}
    </span>
  )
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
          <option key={l} value={l} lang={l}>
            {localeNames[l]}
          </option>
        ))}
      </select>
    </label>
  )
}

export function PageHeader({ title, subtitle, actions }: { title: string; subtitle?: ReactNode; actions?: ReactNode }) {
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

/** Metric card. `icon` sits in a tinted tile; `hint` explains what the number counts. */
export function StatCard({
  label,
  value,
  tone,
  icon: Icon,
  iconTone,
  hint,
  info,
}: {
  label: string
  value: ReactNode
  tone?: string
  icon?: LucideIcon
  iconTone?: 'teal' | 'violet' | 'warning' | 'danger' | 'neutral'
  hint?: ReactNode
  info?: ReactNode
}) {
  return (
    <div className={`stat${tone ? ` stat--${tone}` : ''}`}>
      {Icon && (
        <span className={`icon-tile${iconTone ? ` icon-tile--${iconTone}` : ''}`}>
          <Icon className="icon" aria-hidden="true" />
        </span>
      )}
      <span className="stat__label">
        {label}
        {info}
      </span>
      <span className="stat__value">{value}</span>
      {hint && <span className="stat__hint">{hint}</span>}
    </div>
  )
}

export function Stars({ rating, label }: { rating: number | null; label?: string }) {
  if (rating === null) return null
  const full = Math.round(rating)
  return (
    <span className="stars" aria-label={label ?? String(rating)} role="img">
      {[0, 1, 2, 3, 4].map((i) => (
        <Star key={i} className={i < full ? 'icon' : 'icon stars__empty'} aria-hidden="true" />
      ))}
    </span>
  )
}

export function CopyButton({ text }: { text: string }) {
  const { t } = useI18n()
  const [copied, setCopied] = useState(false)
  return (
    <button
      type="button"
      className="btn btn--ghost btn--small"
      onClick={() => {
        void navigator.clipboard?.writeText(text).then(() => setCopied(true), () => setCopied(false))
      }}
    >
      <span aria-live="polite">{copied ? t('common.copied') : t('common.copy')}</span>
    </button>
  )
}

/** Definition list for read-only detail panels. */
export function Details({ items }: { items: [ReactNode, ReactNode][] }) {
  return (
    <dl className="details">
      {items.map(([k, v], i) => (
        <div key={i} className="details__row">
          <dt>{k}</dt>
          <dd>{v}</dd>
        </div>
      ))}
    </dl>
  )
}
