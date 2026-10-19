import { BadgeCheck, CalendarPlus, Clock, ListChecks, MapPin, Navigation, Phone, Star, Store, UserRoundCheck } from 'lucide-react'
import { useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router'
import { useApi } from '../../api/context'
import type { PublicBranch, PublicServiceOffer, Weekday } from '../../api/types'
import { weekdays } from '../../api/types'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { categoryLabel, isServiceCategory } from '../../lib/categories'
import { formatInstantDate, formatMoney } from '../../lib/format'
import { compareAmounts } from '../../lib/money'
import { DEFAULT_TIME_ZONE, localDate, localMinutes, toMinutes, weekdayOf } from '../../lib/time'
import { useNow } from '../../hooks/useNow'
import { Monogram } from '../../ui/Monogram'
import { Skeleton, SkeletonCards } from '../../ui/Skeleton'
import { categoryIcon, categoryTone } from './categoryIcons'
import { Pagination } from '../../ui/Pagination'
import { ApiErrorState, EmptyState, LoadingState } from '../../ui/states'
import { Stars } from '../../ui/widgets'
import { ReportButton } from './ReportButton'

export function ClinicPage() {
  const { clinicId = '' } = useParams()
  const { t, locale } = useI18n()
  const api = useApi()
  const [params, setParams] = useSearchParams()
  const clinic = useQuery(`clinic:${clinicId}`, () => api.public.clinic(clinicId))
  useTitle(clinic.data?.name ?? '')

  if (clinic.error !== undefined) {
    return (
      <div className="page narrow">
        <ApiErrorState error={clinic.error} onRetry={clinic.reload} />
      </div>
    )
  }
  if (!clinic.data) {
    return (
      <div className="page">
        <Skeleton variant="block" height={160} />
        <SkeletonCards count={3} label={t('common.loading')} />
      </div>
    )
  }
  const c = clinic.data
  // The selected branch is in the URL; every price, hour and slot below belongs to that branch only.
  const branch = c.branches.find((b) => b.id === params.get('branch')) ?? c.branches[0]
  const minPrice = branch?.services.reduce<PublicServiceOffer | undefined>((m, s) => (!m || compareAmounts(s.price.amount, m.price.amount) < 0 ? s : m), undefined)

  return (
    <div className="page clinic-page">
      <nav className="breadcrumbs" aria-label={t('clinic.breadcrumbs')}>
        <Link to="/clinics">{t('nav.clinics')}</Link> <span aria-hidden="true">›</span> <span>{c.name}</span>
      </nav>

      <header className="clinic-hero card">
        <div className="clinic-hero__cover" aria-hidden="true">
          <Monogram name={c.name} seed={c.id} size={88} />
        </div>
        <div className="clinic-hero__body">
          <div className="clinic-hero__title">
            <h1>{c.name}</h1>
            <span className="badge badge--success">
              <BadgeCheck className="icon" aria-hidden="true" />
              {t('catalog.verified')}
            </span>
          </div>
          <p className="clinic-hero__rating">
            {c.rating !== null ? (
              <span className="rating" role="img" aria-label={t('home.ratingLabel', { rating: c.rating.toFixed(1) })}>
                <Star className="icon" aria-hidden="true" />
                {t('home.rating', { rating: c.rating.toFixed(1), count: c.reviewCount })}
              </span>
            ) : (
              <span className="muted">{t('home.noRating')}</span>
            )}
          </p>
          {branch && (
            <ul className="clinic-hero__facts">
              <li>
                <MapPin className="icon" aria-hidden="true" />
                {branch.district}, {branch.address}
              </li>
              <li>
                <Clock className="icon" aria-hidden="true" />
                <OpenNow branch={branch} />
              </li>
              <li>
                <ListChecks className="icon" aria-hidden="true" />
                {t('clinicPage.servicesCount', { count: branch.services.length })}
                {minPrice && <> · {t('home.priceFrom', { price: formatMoney(minPrice.price, locale) })}</>}
              </li>
            </ul>
          )}
        </div>
        <div className="clinic-hero__actions">
          <Link className="btn btn--large" to={`/book/${c.id}${branch ? `?branch=${branch.id}&step=service` : ''}`}>
            <CalendarPlus className="icon" aria-hidden="true" />
            {t('home.book')}
          </Link>
          <ReportButton targetType="Clinic" targetId={c.id} />
        </div>
      </header>

      {c.branches.length > 1 && (
        <div className="clinic-branches" role="group" aria-label={t('clinicPage.chooseBranch')}>
          {c.branches.map((b) => (
            <button
              key={b.id}
              type="button"
              className="chip"
              aria-pressed={b.id === branch?.id}
              onClick={() => setParams((prev) => {
                const next = new URLSearchParams(prev)
                next.set('branch', b.id)
                return next
              }, { replace: true })}
            >
              <Store className="icon" aria-hidden="true" />
              {b.name} · {b.district}
            </button>
          ))}
        </div>
      )}

      {c.description && (
        <section className="stack stack--sm">
          <h2>{t('clinicPage.about')}</h2>
          <p className="lead clinic-page__desc">{c.description}</p>
        </section>
      )}
      <p className="alert alert--info">
        <UserRoundCheck className="icon" aria-hidden="true" />
        <span>{t('clinic.doctorNote')}</span>
      </p>

      {!branch ? <EmptyState title={t('clinic.noBranches')} /> : <BranchSection key={branch.id} clinicId={c.id} branch={branch} />}

      <Reviews clinicId={c.id} />
    </div>
  )
}

/** "Open now until 18:00" in the branch's own time zone (display only). */
function OpenNow({ branch }: { branch: PublicBranch }) {
  const { t } = useI18n()
  const now = useNow(60_000)
  const today = weekdayOf(localDate(now, branch.timeZone))
  const minutes = localMinutes(now, branch.timeZone)
  const open = branch.hours.find((h) => h.weekday === today && toMinutes(h.open) <= minutes && minutes < toMinutes(h.close))
  return open ? <span className="clinic-open">{t('clinicPage.openNow', { close: open.close })}</span> : <span className="clinic-closed">{t('clinicPage.closedNow')}</span>
}

function BranchSection({ clinicId, branch }: { clinicId: string; branch: PublicBranch }) {
  const { t, locale } = useI18n()
  const now = useNow(60_000)
  const today = weekdayOf(localDate(now, branch.timeZone))
  const hoursFor = (d: Weekday) => branch.hours.filter((h) => h.weekday === d)
  const mapUrl =
    branch.latitude !== null && branch.longitude !== null
      ? `https://www.openstreetmap.org/?mlat=${branch.latitude}&mlon=${branch.longitude}#map=17/${branch.latitude}/${branch.longitude}`
      : null
  return (
    <section className="clinic-branch" aria-labelledby={`branch-${branch.id}`}>
      <div className="clinic-branch__main stack">
        <div className="section-head">
          <h2 id={`branch-${branch.id}`}>{t('clinic.services')}</h2>
          <span className="muted small">{branch.name}</span>
        </div>
        {branch.services.length === 0 ? (
          <EmptyState title={t('clinic.noServices')} />
        ) : (
          <ul className="service-list">
            {branch.services.map((s) => {
              const cat = isServiceCategory(s.category) ? s.category : 'other'
              const Icon = categoryIcon[cat]
              const tone = categoryTone[cat]
              return (
                <li key={s.serviceId} className="service-row">
                  <span className={`icon-tile${tone ? ` icon-tile--${tone}` : ''}`}>
                    <Icon className="icon" aria-hidden="true" />
                  </span>
                  <div className="service-row__info">
                    <strong>{s.name}</strong>
                    <span className="muted small">
                      {categoryLabel(t, s.category)} · {t('common.minutes', { minutes: s.durationMinutes })}
                    </span>
                  </div>
                  <div className="service-row__price">
                    {s.priceKind === 'From' ? t('clinic.priceFrom', { price: formatMoney(s.price, locale) }) : formatMoney(s.price, locale)}
                  </div>
                  <Link className="btn btn--subtle btn--small" to={`/book/${clinicId}?branch=${branch.id}&service=${s.serviceId}&step=time`} aria-label={`${t('clinicPage.choose')}: ${s.name}`}>
                    {t('clinicPage.choose')}
                  </Link>
                </li>
              )
            })}
          </ul>
        )}
        <p className="muted small">{t('clinic.priceNote')}</p>
      </div>

      <aside className="clinic-branch__side card stack">
        <h3 className="card__title">{branch.name}</h3>
        <p className="muted small clinic-branch__addr">
          <MapPin className="icon" aria-hidden="true" />
          {branch.district}, {branch.address}
        </p>
        <div className="row">
          {mapUrl && (
            <a className="btn btn--ghost btn--small" href={mapUrl} target="_blank" rel="noopener noreferrer">
              <Navigation className="icon" aria-hidden="true" />
              {t('clinic.map')}
            </a>
          )}
          {branch.phone && (
            <a className="btn btn--ghost btn--small" href={`tel:${branch.phone}`}>
              <Phone className="icon" aria-hidden="true" />
              {t('clinic.call')}
            </a>
          )}
        </div>
        {branch.phone && <p className="small tabular">{branch.phone}</p>}
        <div>
          <h4>{t('clinic.hours')}</h4>
          <table className="table table--compact hours-table">
            <tbody>
              {weekdays.map((d) => {
                const hs = hoursFor(d)
                return (
                  <tr key={d} className={d === today ? 'hours-table__today' : undefined} aria-current={d === today ? 'date' : undefined}>
                    <th scope="row">{t(`weekdayLong.${d}`)}</th>
                    <td className="tabular">{hs.length ? hs.map((h) => `${h.open}–${h.close}`).join(', ') : t('clinic.closed')}</td>
                  </tr>
                )
              })}
            </tbody>
          </table>
          <p className="muted small">{t('clinic.timeZone', { tz: branch.timeZone })}</p>
        </div>
      </aside>
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
