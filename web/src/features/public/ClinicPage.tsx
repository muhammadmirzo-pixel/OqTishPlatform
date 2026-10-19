import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { useApi } from '../../api/context'
import type { PublicBranch, Weekday } from '../../api/types'
import { weekdays } from '../../api/types'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { categoryLabel } from '../../lib/categories'
import { formatInstantDate, formatMoney } from '../../lib/format'
import { DEFAULT_TIME_ZONE } from '../../lib/time'
import { Pagination } from '../../ui/Pagination'
import { ApiErrorState, EmptyState, LoadingState } from '../../ui/states'
import { Stars } from '../../ui/widgets'
import { ReportButton } from './ReportButton'

export function ClinicPage() {
  const { clinicId = '' } = useParams()
  const { t } = useI18n()
  const api = useApi()
  const clinic = useQuery(`clinic:${clinicId}`, () => api.public.clinic(clinicId))
  useTitle(clinic.data?.name ?? '')

  if (clinic.error !== undefined) {
    return (
      <div className="page narrow">
        <ApiErrorState error={clinic.error} onRetry={clinic.reload} />
      </div>
    )
  }
  if (!clinic.data) return <LoadingState />
  const c = clinic.data

  return (
    <div className="page">
      <nav className="breadcrumbs" aria-label={t('clinic.breadcrumbs')}>
        <Link to="/clinics">{t('nav.clinics')}</Link> <span aria-hidden="true">›</span> <span>{c.name}</span>
      </nav>
      <header className="page-header">
        <div>
          <h1>{c.name}</h1>
          <p>
            {c.rating !== null ? (
              <>
                <Stars rating={c.rating} label={t('home.ratingLabel', { rating: c.rating.toFixed(1) })} /> {t('home.rating', { rating: c.rating.toFixed(1), count: c.reviewCount })}
              </>
            ) : (
              <span className="muted">{t('home.noRating')}</span>
            )}
          </p>
        </div>
        <div className="page-header__actions">
          <Link className="btn" to={`/book/${c.id}`}>
            {t('home.book')}
          </Link>
          <ReportButton targetType="Clinic" targetId={c.id} />
        </div>
      </header>
      {c.description && <p className="lead">{c.description}</p>}
      <p className="muted small">{t('clinic.doctorNote')}</p>

      <h2>{t('clinic.branches')}</h2>
      {c.branches.length === 0 ? (
        <EmptyState title={t('clinic.noBranches')} />
      ) : (
        <div className="stack">
          {c.branches.map((b) => (
            <BranchSection key={b.id} clinicId={c.id} branch={b} />
          ))}
        </div>
      )}

      <Reviews clinicId={c.id} />
    </div>
  )
}

function BranchSection({ clinicId, branch }: { clinicId: string; branch: PublicBranch }) {
  const { t, locale } = useI18n()
  const hoursFor = (d: Weekday) => branch.hours.filter((h) => h.weekday === d)
  const mapUrl =
    branch.latitude !== null && branch.longitude !== null
      ? `https://www.openstreetmap.org/?mlat=${branch.latitude}&mlon=${branch.longitude}#map=17/${branch.latitude}/${branch.longitude}`
      : null
  return (
    <section className="card branch" aria-labelledby={`branch-${branch.id}`}>
      <header className="row row--between">
        <div>
          <h3 id={`branch-${branch.id}`}>{branch.name}</h3>
          <p className="muted">
            {branch.district}, {branch.address}
          </p>
        </div>
        <div className="row">
          {branch.phone && (
            <a className="btn btn--ghost btn--small" href={`tel:${branch.phone}`}>
              {t('clinic.call')}
            </a>
          )}
          {mapUrl && (
            <a className="btn btn--ghost btn--small" href={mapUrl} target="_blank" rel="noopener noreferrer">
              {t('clinic.map')}
            </a>
          )}
          <Link className="btn btn--small" to={`/book/${clinicId}?branch=${branch.id}`}>
            {t('home.book')}
          </Link>
        </div>
      </header>
      <div className="branch__grid">
        <div>
          <h4>{t('clinic.hours')}</h4>
          <table className="table table--compact">
            <tbody>
              {weekdays.map((d) => {
                const hs = hoursFor(d)
                return (
                  <tr key={d}>
                    <th scope="row">{t(`weekdayLong.${d}`)}</th>
                    <td>{hs.length ? hs.map((h) => `${h.open}–${h.close}`).join(', ') : t('clinic.closed')}</td>
                  </tr>
                )
              })}
            </tbody>
          </table>
          <p className="muted small">{t('clinic.timeZone', { tz: branch.timeZone })}</p>
        </div>
        <div>
          <h4>{t('clinic.services')}</h4>
          {branch.services.length === 0 ? (
            <p className="muted">{t('clinic.noServices')}</p>
          ) : (
            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th scope="col">{t('common.service')}</th>
                    <th scope="col">{t('clinic.duration')}</th>
                    <th scope="col" className="num">
                      {t('clinic.price')}
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {branch.services.map((s) => (
                    <tr key={s.serviceId}>
                      <td>
                        {s.name}
                        <div className="muted small">{categoryLabel(t, s.category)}</div>
                      </td>
                      <td>{t('common.minutes', { minutes: s.durationMinutes })}</td>
                      <td className="num">
                        {s.priceKind === 'From' ? t('clinic.priceFrom', { price: formatMoney(s.price, locale) }) : formatMoney(s.price, locale)}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          <p className="muted small">{t('clinic.priceNote')}</p>
        </div>
      </div>
    </section>
  )
}

function Reviews({ clinicId }: { clinicId: string }) {
  const { t, locale } = useI18n()
  const api = useApi()
  const [page, setPage] = useState(1)
  const reviews = useQuery(`reviews:${clinicId}:${page}`, () => api.public.reviews(clinicId, page))
  return (
    <section aria-labelledby="reviews-title" className="stack">
      <h2 id="reviews-title">{t('clinic.reviews')}</h2>
      {reviews.error !== undefined ? (
        <ApiErrorState error={reviews.error} onRetry={reviews.reload} />
      ) : !reviews.data ? (
        <LoadingState />
      ) : reviews.data.items.length === 0 ? (
        <EmptyState title={t('clinic.noReviews')} />
      ) : (
        <>
          <ul className="stack list-plain">
            {reviews.data.items.map((r) => (
              <li key={r.id} className="card review">
                <div className="row row--between">
                  <strong>{r.authorName}</strong>
                  <Stars rating={r.rating} label={t('home.ratingLabel', { rating: r.rating })} />
                </div>
                <p>{r.text}</p>
                <div className="row row--between">
                  <span className="muted small">
                    {r.branchName} · {formatInstantDate(r.createdAt, DEFAULT_TIME_ZONE, locale)}
                  </span>
                  <ReportButton targetType="Review" targetId={r.id} />
                </div>
              </li>
            ))}
          </ul>
          <Pagination page={reviews.data.page} pageSize={reviews.data.pageSize} totalCount={reviews.data.totalCount} onPage={setPage} />
        </>
      )}
    </section>
  )
}
