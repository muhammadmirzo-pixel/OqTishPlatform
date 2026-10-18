import type { AppointmentHistoryEntry } from '../../api/types'
import { useI18n } from '../../i18n/context'
import { formatFullDateTime } from '../../lib/format'
import { reasonLabel } from './format'
import { Countdown, StatusBadge } from '../../ui/widgets'

/** Display-only countdown to the server's Pending deadline. */
export function PendingCountdown({ expiresAt, variant = 'patient' }: { expiresAt: string; variant?: 'patient' | 'clinic' }) {
  const { t } = useI18n()
  return (
    <Countdown
      deadline={expiresAt}
      render={(time) => t(variant === 'patient' ? 'appt.expiresIn' : 'req.deadline', { time })}
      expiredText={t(variant === 'patient' ? 'appt.expiredWaiting' : 'req.expired')}
    />
  )
}

export function StatusHistory({ history, timeZone }: { history: AppointmentHistoryEntry[] | undefined; timeZone: string }) {
  const { t, locale } = useI18n()
  if (!history || history.length === 0) return null
  return (
    <section aria-labelledby="history-title">
      <h2 id="history-title">{t('appt.history')}</h2>
      <ol className="timeline">
        {history.map((h, i) => (
          <li key={`${h.at}-${i}`} className="timeline__item">
            <StatusBadge status={h.to} />
            <span className="timeline__meta">
              {formatFullDateTime(h.at, timeZone, locale)} · {t(`actor.${h.actor}`)}
              {h.reasonCode && <> · {reasonLabel(t, h.reasonCode)}</>}
            </span>
          </li>
        ))}
      </ol>
    </section>
  )
}

