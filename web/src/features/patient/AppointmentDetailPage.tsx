import { useState, type FormEvent } from 'react'
import { Link, useLocation, useParams } from 'react-router'
import { isApiError } from '../../api/client'
import { useApi } from '../../api/context'
import type { AppointmentDto } from '../../api/types'
import { useAction } from '../../hooks/useAction'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { formatFullDateTime, formatMoney } from '../../lib/format'
import { formatPhone } from '../../lib/phone'
import { Field, FormError, Notice, SubmitButton } from '../../ui/forms'
import { ConfirmDialog } from '../../ui/ReasonDialog'
import { QueryView } from '../../ui/states'
import { Details, StatusBadge } from '../../ui/widgets'
import { ReportButton } from '../public/ReportButton'
import { PendingCountdown, StatusHistory } from '../appointments/shared'
import { appointmentWhen, durationMinutes } from '../appointments/format'
import { useNow } from '../../hooks/useNow'

const REVIEW_WINDOW_DAYS = 30

export function AppointmentDetailPage({ basePath = '/me/appointments' }: { basePath?: string }) {
  const { id = '' } = useParams()
  const { t } = useI18n()
  const api = useApi()
  const location = useLocation()
  const justBooked = Boolean((location.state as { justBooked?: boolean } | null)?.justBooked)
  const query = useQuery(`my-appointment:${id}`, () => api.patient.get(id))
  useTitle(query.data?.serviceName ?? t('appt.myTitle'))

  return (
    <div className="page narrow">
      <nav className="breadcrumbs" aria-label={t('clinic.breadcrumbs')}>
        <Link to={basePath}>{t('appt.myTitle')}</Link>
      </nav>
      <QueryView query={query}>
        {(a) => <PatientAppointment appointment={a} justBooked={justBooked} onChanged={(next) => (next ? query.setData(next) : query.reload())} />}
      </QueryView>
    </div>
  )
}

export function PatientAppointment({ appointment: a, justBooked, onChanged, compact }: { appointment: AppointmentDto; justBooked?: boolean; onChanged: (next?: AppointmentDto) => void; compact?: boolean }) {
  const now = useNow(60_000)
  const { t, locale } = useI18n()
  const api = useApi()
  const [cancelling, setCancelling] = useState(false)
  const reviewable = a.status === 'Completed' && now - new Date(a.endsAt).getTime() < REVIEW_WINDOW_DAYS * 86400000

  return (
    <article className="stack">
      {justBooked && <Notice>{t('appt.justBooked')}</Notice>}
      <header className="page-header">
        <div>
          <h1>{a.serviceName}</h1>
          <p className="muted">
            {a.clinicName} · {a.branchName}
          </p>
        </div>
        <StatusBadge status={a.status} />
      </header>

      {a.status === 'Pending' && a.expiresAt && (
        <div className="alert alert--info" role="note">
          <p>
            <strong>
              <PendingCountdown expiresAt={a.expiresAt} />
            </strong>
          </p>
          <p className="small">{t('appt.pendingExplain')}</p>
        </div>
      )}

      <Details
        items={[
          [t('book.when'), appointmentWhen(a, locale)],
          [t('clinic.duration'), t('common.minutes', { minutes: durationMinutes(a) })],
          [t('common.doctor'), a.doctor ? a.doctor.name : t('appt.doctorPending')],
          [t('appt.quotedPrice'), formatMoney(a.quotedPrice, locale)],
          ...(a.chargedAmount ? [[t('appt.charged'), formatMoney(a.chargedAmount, locale)] as [string, string]] : []),
          [t('book.contact'), `${a.contactName} · ${formatPhone(a.contactPhone)}`],
          [t('common.createdAt'), formatFullDateTime(a.createdAt, a.timeZone, locale)],
        ]}
      />
      <p className="muted small">{t('appt.timeZoneNote', { tz: a.timeZone })}</p>

      <div className="row">
        {a.canCancel && (
          <button type="button" className="btn btn--danger" onClick={() => setCancelling(true)}>
            {t('appt.cancel')}
          </button>
        )}
        {!compact && (
          <>
            <Link className="btn btn--ghost" to={`/clinics/${a.clinicId}`}>
              {t('appt.clinicPage')}
            </Link>
            <ReportButton targetType="Clinic" targetId={a.clinicId} />
          </>
        )}
      </div>
      {a.status === 'Confirmed' && !a.canCancel && <p className="muted small">{t('appt.cancelCutoff')}</p>}

      {cancelling && (
        <ConfirmDialog
          title={t('appt.cancelTitle')}
          body={<p>{t(a.status === 'Pending' ? 'appt.cancelPendingBody' : 'appt.cancelConfirmedBody')}</p>}
          confirmLabel={t('appt.cancelYes')}
          danger
          onConfirm={() => api.patient.cancel(a.id)}
          onDone={() => {
            setCancelling(false)
            onChanged()
          }}
          onClose={() => setCancelling(false)}
        />
      )}

      {reviewable && <ReviewForm appointmentId={a.id} />}
      <StatusHistory history={a.history} timeZone={a.timeZone} />
    </article>
  )
}

function ReviewForm({ appointmentId }: { appointmentId: string }) {
  const { t } = useI18n()
  const api = useApi()
  const [rating, setRating] = useState(0)
  const [text, setText] = useState('')
  const [touched, setTouched] = useState(false)
  const [done, setDone] = useState(false)
  const action = useAction(() => api.patient.review(appointmentId, { rating, text: text.trim() }))
  const ratingError = rating < 1 ? t('review.ratingRequired') : null
  const textError = text.length > 1000 ? t('valid.maxLength', { max: 1000 }) : null

  if (done) return <Notice>{t('review.thanks')}</Notice>
  if (isApiError(action.error, 'review.already_exists')) return <Notice tone="info">{t('err.review.already_exists')}</Notice>

  async function submit(e: FormEvent) {
    e.preventDefault()
    setTouched(true)
    if (ratingError || textError) return
    const r = await action.run()
    if (r.ok) setDone(true)
  }

  return (
    <section className="card stack" aria-labelledby="review-title">
      <h2 id="review-title">{t('review.title')}</h2>
      <p className="muted small">{t('review.note')}</p>
      <form onSubmit={submit} noValidate className="stack">
        <fieldset className="fieldset">
          <legend>{t('review.rating')}</legend>
          <div className="rating-input">
            {[1, 2, 3, 4, 5].map((n) => (
              <label key={n} className={n <= rating ? 'rating-input__star rating-input__star--on' : 'rating-input__star'}>
                <input type="radio" name="rating" value={n} checked={rating === n} onChange={() => setRating(n)} />
                <span aria-hidden="true">★</span>
                <span className="visually-hidden">{t('review.stars', { count: n })}</span>
              </label>
            ))}
          </div>
          {touched && ratingError && <p className="field__error">{ratingError}</p>}
        </fieldset>
        <Field label={t('review.text')} hint={t('review.textHint', { count: 1000 - text.length })} error={touched ? textError : null} optional>
          {(p) => <textarea {...p} rows={4} maxLength={1000} value={text} onChange={(e) => setText(e.target.value)} />}
        </Field>
        <FormError error={action.error} />
        <SubmitButton pending={action.pending}>{t('review.submit')}</SubmitButton>
      </form>
    </section>
  )
}
