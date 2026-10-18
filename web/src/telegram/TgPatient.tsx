import { useCallback, useState, type FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { useApi } from '../api/context'
import { useQuery } from '../hooks/useQuery'
import { useI18n } from '../i18n/context'
import { formatMoney } from '../lib/format'
import { EmptyState, QueryView } from '../ui/states'
import { StatusBadge } from '../ui/widgets'
import { PendingCountdown } from '../features/appointments/shared'
import { appointmentWhen } from '../features/appointments/format'
import { BookingWizard, type BookingState } from '../features/booking/BookingWizard'
import { PatientAppointment } from '../features/patient/AppointmentDetailPage'
import { TgMainButton } from './TgMainButton'

export function TgHome() {
  const { t, locale } = useI18n()
  const api = useApi()
  const [q, setQ] = useState('')
  const [query, setQuery] = useState('')
  const results = useQuery(`tg-clinics:${query}`, () => api.public.searchClinics({ q: query || undefined, sort: 'rating', page: 1, pageSize: 20 }))

  function submit(e: FormEvent) {
    e.preventDefault()
    setQuery(q.trim())
  }

  return (
    <div className="stack">
      <h1>{t('home.title')}</h1>
      <form className="search" role="search" onSubmit={submit}>
        <label className="visually-hidden" htmlFor="tg-q">
          {t('home.searchPlaceholder')}
        </label>
        <input id="tg-q" type="search" value={q} placeholder={t('home.searchPlaceholder')} onChange={(e) => setQ(e.target.value)} />
        <button type="submit" className="btn">
          {t('common.search')}
        </button>
      </form>
      <QueryView query={results}>
        {(page) =>
          page.items.length === 0 ? (
            <EmptyState title={t('home.empty')} />
          ) : (
            <ul className="list-plain stack">
              {page.items.map((c) => (
                <li key={c.id}>
                  <Link to={`/tg/clinics/${c.id}`} className="card card--link stack">
                    <strong>{c.name}</strong>
                    <span className="muted small">
                      {c.districts.join(', ')}
                      {c.rating !== null && <> · ★ {c.rating.toFixed(1)}</>}
                    </span>
                    {c.minPrice && <span className="small">{t('home.priceFrom', { price: formatMoney(c.minPrice, locale) })}</span>}
                  </Link>
                </li>
              ))}
            </ul>
          )
        }
      </QueryView>
    </div>
  )
}

export function TgBook() {
  const { clinicId = '' } = useParams()
  const navigate = useNavigate()
  const [state, setState] = useState<BookingState>({ step: 'branch' })
  const onChange = useCallback((next: BookingState) => setState(next), [])
  return (
    <BookingWizard
      clinicId={clinicId}
      state={state}
      onChange={onChange}
      loginHref={null}
      renderSubmit={(props) => <TgMainButton {...props} />}
      onBooked={(a) => navigate(`/tg/appointments/${a.id}`, { replace: true, state: { justBooked: true } })}
    />
  )
}

export function TgMine() {
  const { t, locale } = useI18n()
  const api = useApi()
  const list = useQuery('tg-mine', () => api.patient.list({ page: 1 }), { refreshMs: 30000 })
  return (
    <div className="stack">
      <h1>{t('appt.myTitle')}</h1>
      <QueryView query={list}>
        {(page) =>
          page.items.length === 0 ? (
            <EmptyState title={t('appt.empty')}>
              <Link className="btn" to="/tg">
                {t('appt.bookNew')}
              </Link>
            </EmptyState>
          ) : (
            <ul className="list-plain stack">
              {page.items.map((a) => (
                <li key={a.id}>
                  <Link to={`/tg/appointments/${a.id}`} className="card card--link stack">
                    <span className="row row--between">
                      <strong>{a.serviceName}</strong>
                      <StatusBadge status={a.status} />
                    </span>
                    <span className="small">{a.clinicName}</span>
                    <span className="muted small">{appointmentWhen(a, locale)}</span>
                    {a.status === 'Pending' && a.expiresAt && <PendingCountdown expiresAt={a.expiresAt} />}
                  </Link>
                </li>
              ))}
            </ul>
          )
        }
      </QueryView>
    </div>
  )
}

export function TgAppointment() {
  const { id = '' } = useParams()
  const api = useApi()
  const query = useQuery(`my-appointment:${id}`, () => api.patient.get(id))
  return <QueryView query={query}>{(a) => <PatientAppointment appointment={a} compact onChanged={(next) => (next ? query.setData(next) : query.reload())} />}</QueryView>
}
