import { CalendarPlus, RotateCcw } from 'lucide-react'
import { Link } from 'react-router'
import type { AppointmentDto, AppointmentStatus } from '../../api/types'
import { useI18n } from '../../i18n/context'
import { CountdownRing } from '../../ui/CountdownRing'
import { StatusBadge, StatusIcon } from '../../ui/widgets'
import { PendingCountdown } from '../appointments/shared'

const tone: Record<AppointmentStatus, string> = {
  Pending: 'warning',
  Confirmed: '',
  Completed: 'teal',
  Rejected: 'neutral',
  CancelledByPatient: 'neutral',
  CancelledByClinic: 'neutral',
  Expired: 'neutral',
  NoShow: 'danger',
}

/** Statuses after which the useful next step is choosing a new time at the same clinic. */
const rebook: Partial<Record<AppointmentStatus, 'scene.pickAnother' | 'scene.bookAgain'>> = {
  Rejected: 'scene.pickAnother',
  Expired: 'scene.pickAnother',
  CancelledByClinic: 'scene.pickAnother',
  CancelledByPatient: 'scene.bookAgain',
  NoShow: 'scene.bookAgain',
  Completed: 'scene.bookAgain',
}

/**
 * Headline block of an appointment: what happened and what to do next. Everything shown is the
 * server's status; the ring only visualises time left until the server's deadline.
 */
export function StatusScene({ appointment: a }: { appointment: AppointmentDto }) {
  const { t } = useI18n()
  const toneClass = tone[a.status]
  const again = rebook[a.status]
  const totalMs = a.expiresAt ? new Date(a.expiresAt).getTime() - new Date(a.createdAt).getTime() : 0
  return (
    <section className={`scene scene--${a.status}`} aria-labelledby="scene-title" role="status" aria-live="polite">
      <div className="scene__visual" key={a.status}>
        {a.status === 'Pending' && a.expiresAt ? (
          <CountdownRing deadline={a.expiresAt} totalMs={totalMs} size={88} expiredText="0:00" />
        ) : (
          <span className={`icon-tile icon-tile--lg scene__icon${toneClass ? ` icon-tile--${toneClass}` : ''}`}>
            <StatusIcon status={a.status} />
          </span>
        )}
      </div>
      <div className="scene__text">
        <StatusBadge status={a.status} />
        <h2 id="scene-title">{t(`scene.${a.status}.title`)}</h2>
        <p className="muted">{t(`scene.${a.status}.body`)}</p>
        {a.status === 'Pending' && a.expiresAt && (
          <p className="scene__countdown">
            <strong>
              <PendingCountdown expiresAt={a.expiresAt} />
            </strong>
          </p>
        )}
        {again && (
          <div className="row">
            <Link className="btn" to={`/book/${a.clinicId}?branch=${a.branchId}&service=${a.serviceId}&step=time`}>
              {again === 'scene.bookAgain' ? <RotateCcw className="icon" aria-hidden="true" /> : <CalendarPlus className="icon" aria-hidden="true" />}
              {t(again)}
            </Link>
          </div>
        )}
      </div>
    </section>
  )
}
