import { CalendarClock, CheckCircle2, UserX, XCircle } from 'lucide-react'
import { useState, type ReactNode } from 'react'
import { Link, useParams } from 'react-router'
import { errorMessage } from '../../api/errors'
import { useApi } from '../../api/context'
import type { AppointmentDto } from '../../api/types'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { formatFullDateTime, formatMoney } from '../../lib/format'
import { formatPhone } from '../../lib/phone'
import { Notice } from '../../ui/forms'
import { ConfirmDialog } from '../../ui/ReasonDialog'
import { QueryView } from '../../ui/states'
import { useToast } from '../../ui/toastContext'
import { Details, StatusBadge } from '../../ui/widgets'
import { AppointmentFinance } from '../finance/AppointmentFinance'
import { FinanceTerm } from '../finance/FinanceTerm'
import { PendingCountdown, StatusHistory } from '../appointments/shared'
import { appointmentWhen, cancelReasons } from '../appointments/format'
import { useClinic } from './clinicContext'
import { CompleteDialog, RescheduleDialog } from './OutcomeDialogs'
import { PendingActions } from './PendingActions'
import { Perm } from './permissions'
import { ReasonCodeDialog } from './ReasonCodeDialog'

type DialogKind = 'cancel' | 'reschedule' | 'complete' | 'noShow' | null

export function ClinicAppointmentPage() {
  const { appointmentId = '' } = useParams()
  const { t } = useI18n()
  const api = useApi()
  const { clinicId } = useClinic()
  const query = useQuery(`clinic-appointment:${clinicId}:${appointmentId}`, () => api.clinic.appointment(clinicId, appointmentId))
  useTitle(query.data?.serviceName ?? t('clinicNav.appointments'))

  return (
    <div className="stack">
      <nav className="breadcrumbs" aria-label={t('clinic.breadcrumbs')}>
        <Link to={`/clinic/${clinicId}/appointments`}>{t('clinicNav.appointments')}</Link>
      </nav>
      <QueryView query={query}>{(a) => <AppointmentView appointment={a} reload={query.reload} />}</QueryView>
    </div>
  )
}

function AppointmentView({ appointment: a, reload }: { appointment: AppointmentDto; reload: () => void }) {
  const { t, locale } = useI18n()
  const api = useApi()
  const { clinicId, can } = useClinic()
  const [dialog, setDialog] = useState<DialogKind>(null)
  // Results are echoed in a toast; a stale request (already handled or expired) stays on the page.
  const [stale, setStale] = useState<string | null>(null)
  const toast = useToast()
  const done = (message: string) => {
    setDialog(null)
    setStale(null)
    toast({ title: message })
    reload()
  }
  const confirmed = a.status === 'Confirmed'
  const showFinance = can(Perm.paymentRecord, Perm.reportView) && a.status !== 'Pending'

  return (
    <article className="stack">
      <header className="page-header">
        <div>
          <h1>{a.serviceName}</h1>
          <p className="muted">
            {appointmentWhen(a, locale)} · {a.branchName}
          </p>
        </div>
        <StatusBadge status={a.status} />
      </header>
      <div aria-live="polite">{stale && <Notice tone="warning">{stale}</Notice>}</div>

      <Details
        items={[
          [t('common.patient'), a.contactName],
          [t('common.phone'), <a key="p" href={`tel:${a.contactPhone}`}>{formatPhone(a.contactPhone)}</a>],
          [t('common.doctor'), a.doctor?.name ?? '—'],
          [t('appt.quotedPrice'), formatMoney(a.quotedPrice, locale)],
          ...(a.chargedAmount ? [[<FinanceTerm key="c" term="charged" />, formatMoney(a.chargedAmount, locale)] as [ReactNode, ReactNode]] : []),
          [t('act.source'), t(`source.${a.source}`)],
          [t('common.createdAt'), formatFullDateTime(a.createdAt, a.timeZone, locale)],
        ]}
      />

      {a.status === 'Pending' && (
        <section className="card stack">
          {a.expiresAt && (
            <p>
              <PendingCountdown expiresAt={a.expiresAt} variant="clinic" />
            </p>
          )}
          {can(Perm.triage) ? (
            <PendingActions
              clinicId={clinicId}
              appointment={a}
              onOutcome={(o) => {
                if (o.kind === 'stale') {
                  setStale(errorMessage(t, o.error))
                  reload()
                } else done(t(o.kind === 'confirmed' ? 'req.confirmed' : 'req.rejected'))
              }}
            />
          ) : (
            <p className="muted">{t('act.noTriage')}</p>
          )}
        </section>
      )}

      {confirmed && (
        <div className="row">
          {can(Perm.outcome) && (
            <>
              <button type="button" className="btn" onClick={() => setDialog('complete')}>
                <CheckCircle2 className="icon" aria-hidden="true" />
                {t('act.complete')}
              </button>
              <button type="button" className="btn btn--ghost" onClick={() => setDialog('noShow')}>
                <UserX className="icon" aria-hidden="true" />
                {t('act.noShow')}
              </button>
            </>
          )}
          {can(Perm.triage) && (
            <button type="button" className="btn btn--ghost" onClick={() => setDialog('reschedule')}>
              <CalendarClock className="icon" aria-hidden="true" />
              {t('act.reschedule')}
            </button>
          )}
          {can(Perm.cancel) && (
            <button type="button" className="btn btn--danger" onClick={() => setDialog('cancel')}>
              <XCircle className="icon" aria-hidden="true" />
              {t('act.cancel')}
            </button>
          )}
        </div>
      )}
      {confirmed && <p className="muted small">{t('act.outcomeNote')}</p>}

      {dialog === 'complete' && <CompleteDialog clinicId={clinicId} appointment={a} onDone={() => done(t('act.completed'))} onClose={() => setDialog(null)} />}
      {dialog === 'reschedule' && <RescheduleDialog clinicId={clinicId} appointment={a} onDone={() => done(t('act.rescheduled'))} onClose={() => setDialog(null)} />}
      {dialog === 'cancel' && (
        <ReasonCodeDialog
          title={t('act.cancel')}
          codes={cancelReasons}
          submitLabel={t('act.cancel')}
          onSubmit={(code) => api.clinic.cancel(clinicId, a.id, code)}
          onDone={() => done(t('act.cancelled'))}
          onClose={() => setDialog(null)}
        />
      )}
      {dialog === 'noShow' && (
        <ConfirmDialog
          title={t('act.noShow')}
          body={<p>{t('act.noShowBody')}</p>}
          confirmLabel={t('act.noShow')}
          onConfirm={() => api.clinic.noShow(clinicId, a.id)}
          onDone={() => done(t('act.noShowDone'))}
          onClose={() => setDialog(null)}
        />
      )}

      {showFinance && <AppointmentFinance appointment={a} />}
      <StatusHistory history={a.history} timeZone={a.timeZone} />
    </article>
  )
}
