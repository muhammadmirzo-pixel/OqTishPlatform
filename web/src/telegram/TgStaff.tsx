import { useState } from 'react'
import { errorMessage } from '../api/errors'
import { useApi } from '../api/context'
import type { Membership } from '../api/types'
import { useSession } from '../auth/session'
import { ClinicContext, makeClinicContext } from '../features/clinic/clinicContext'
import { PendingActions, type TriageOutcome } from '../features/clinic/PendingActions'
import { Perm } from '../features/clinic/permissions'
import { PendingCountdown } from '../features/appointments/shared'
import { appointmentWhen } from '../features/appointments/format'
import { useQuery } from '../hooks/useQuery'
import { useI18n } from '../i18n/context'
import { formatTime } from '../lib/format'
import { formatPhone } from '../lib/phone'
import { localDate, localRangeToUtc } from '../lib/time'
import { Notice } from '../ui/forms'
import { EmptyState, QueryView } from '../ui/states'
import { StatusBadge } from '../ui/widgets'
import { useNow } from '../hooks/useNow'

/** Compact staff view: pending requests (confirm/reject) and today's appointments per clinic. */
export function TgStaff() {
  const { t } = useI18n()
  const { me } = useSession()
  const memberships = me?.memberships ?? []
  if (memberships.length === 0) return <EmptyState title={t('tg.noClinics')} />
  return (
    <div className="stack">
      <h1>{t('tg.staffTitle')}</h1>
      <p className="muted small">{t('tg.staffHint')}</p>
      {memberships.map((m) => (
        <ClinicContext.Provider key={m.clinicId} value={makeClinicContext(m, [], () => undefined)}>
          <StaffClinic membership={m} showName={memberships.length > 1} />
        </ClinicContext.Provider>
      ))}
      <a className="btn btn--ghost" href="/clinic" target="_blank" rel="noopener noreferrer">
        {t('tg.openPanel')}
      </a>
    </div>
  )
}

function StaffClinic({ membership: m, showName }: { membership: Membership; showName: boolean }) {
  const now = useNow(60_000)
  const { t, locale } = useI18n()
  const api = useApi()
  const [notice, setNotice] = useState<string | null>(null)
  const canTriage = m.permissions.includes(Perm.triage)
  const canView = m.permissions.includes(Perm.appointmentView)
  const pending = useQuery(canTriage ? `tg-pending:${m.clinicId}` : null, () => api.clinic.appointments(m.clinicId, { status: 'Pending', pageSize: 50 }), { refreshMs: 30000 })
  const today = localDate(now, 'Asia/Tashkent')
  const range = localRangeToUtc(today, today, 'Asia/Tashkent')
  const todayList = useQuery(canView ? `tg-today:${m.clinicId}:${today}` : null, () => api.clinic.appointments(m.clinicId, { ...range, pageSize: 100 }), { refreshMs: 60000 })

  const onOutcome = (o: TriageOutcome) => {
    setNotice(o.kind === 'stale' ? errorMessage(t, o.error) : t(o.kind === 'confirmed' ? 'req.confirmed' : 'req.rejected'))
    pending.reload()
    todayList.reload()
  }

  return (
    <section className="stack" aria-label={m.clinicName}>
      {showName && <h2>{m.clinicName}</h2>}
      <div aria-live="polite">{notice && <Notice>{notice}</Notice>}</div>
      {canTriage && (
        <>
          <h3>{t('req.title')}</h3>
          <QueryView query={pending}>
            {(page) =>
              page.items.length === 0 ? (
                <p className="muted">{t('req.empty')}</p>
              ) : (
                <ul className="list-plain stack">
                  {page.items.map((a) => (
                    <li key={a.id} className="card stack">
                      <span className="row row--between">
                        <strong>{a.serviceName}</strong>
                        {a.expiresAt && (
                          <span className="badge badge--warning">
                            <PendingCountdown expiresAt={a.expiresAt} variant="clinic" />
                          </span>
                        )}
                      </span>
                      <span className="small">
                        {appointmentWhen(a, locale)} · {a.branchName}
                      </span>
                      <span className="small">{a.contactName}</span>
                      <PendingActions clinicId={m.clinicId} appointment={a} onOutcome={onOutcome} />
                    </li>
                  ))}
                </ul>
              )
            }
          </QueryView>
        </>
      )}
      {canView && (
        <>
          <h3>{t('tg.today')}</h3>
          <QueryView query={todayList}>
            {(page) => {
              const items = page.items.filter((a) => a.status !== 'Pending').sort((a, b) => a.startsAt.localeCompare(b.startsAt))
              return items.length === 0 ? (
                <p className="muted">{t('tg.noToday')}</p>
              ) : (
                <ul className="list-plain stack">
                  {items.map((a) => (
                    <li key={a.id} className="card row row--between">
                      <span>
                        <strong>{formatTime(a.startsAt, a.timeZone, locale)}</strong> {a.serviceName}
                        <div className="muted small">
                          {a.contactName} · <a href={`tel:${a.contactPhone}`}>{formatPhone(a.contactPhone)}</a> · {a.doctor?.name ?? '—'}
                        </div>
                      </span>
                      <StatusBadge status={a.status} />
                    </li>
                  ))}
                </ul>
              )
            }}
          </QueryView>
        </>
      )}
    </section>
  )
}
