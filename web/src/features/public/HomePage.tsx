import { useState, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router'
import { useApi } from '../../api/context'
import type { ClinicCard, ClinicSearchParams, ClinicSort } from '../../api/types'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { formatDateTime, formatMoney } from '../../lib/format'
import { DEFAULT_TIME_ZONE } from '../../lib/time'
import { Checkbox } from '../../ui/forms'
import { Pagination } from '../../ui/Pagination'
import { ApiErrorState, EmptyState, LoadingState } from '../../ui/states'
import { Stars } from '../../ui/widgets'

const sorts: ClinicSort[] = ['rating', 'price', 'distance', 'name']
const PAGE_SIZE = 12

type Coords = { lat: number; lng: number }

export function HomePage() {
  const { t } = useI18n()
  useTitle(t('home.title'))
  const api = useApi()
  const [params, setParams] = useSearchParams()
  const [coords, setCoords] = useState<Coords | null>(null)
  const [geoState, setGeoState] = useState<'idle' | 'locating' | 'denied'>('idle')
  const [q, setQ] = useState(params.get('q') ?? '')

  const sortParam = params.get('sort')
  const sort: ClinicSort = sorts.includes(sortParam as ClinicSort) ? (sortParam as ClinicSort) : 'rating'
  // Distance sort needs coordinates, which are only requested on an explicit user action and kept in memory.
  const effectiveSort: ClinicSort = sort === 'distance' && !coords ? 'rating' : sort
  const search: ClinicSearchParams = {
    q: params.get('q') ?? undefined,
    serviceCategory: params.get('category') ?? undefined,
    serviceId: params.get('service') ?? undefined,
    district: params.get('district') ?? undefined,
    availableWithinDays: params.get('soon') === '1' ? 7 : undefined,
    sort: effectiveSort,
    ...(effectiveSort === 'distance' && coords ? { lat: Number(coords.lat.toFixed(4)), lng: Number(coords.lng.toFixed(4)) } : {}),
    page: Number(params.get('page')) || 1,
    pageSize: PAGE_SIZE,
  }

  const facets = useQuery('facets', () => api.public.facets())
  const results = useQuery(`clinics:${JSON.stringify(search)}`, () => api.public.searchClinics(search))

  function update(changes: Record<string, string | null>) {
    setParams((prev) => {
      const next = new URLSearchParams(prev)
      for (const [k, v] of Object.entries(changes)) {
        if (v) next.set(k, v)
        else next.delete(k)
      }
      if (!('page' in changes)) next.delete('page')
      return next
    })
  }

  function chooseSort(value: ClinicSort) {
    if (value !== 'distance') {
      update({ sort: value === 'rating' ? null : value })
      return
    }
    if (!('geolocation' in navigator)) {
      setGeoState('denied')
      return
    }
    setGeoState('locating')
    navigator.geolocation.getCurrentPosition(
      (pos) => {
        setCoords({ lat: pos.coords.latitude, lng: pos.coords.longitude })
        setGeoState('idle')
        update({ sort: 'distance' })
      },
      () => setGeoState('denied'),
      { maximumAge: 600000, timeout: 15000 },
    )
  }

  function submit(e: FormEvent) {
    e.preventDefault()
    update({ q: q.trim() || null })
  }

  const category = params.get('category') ?? ''
  const services = (facets.data?.services ?? []).filter((s) => !category || s.category === category)

  return (
    <div className="page">
      <section className="hero">
        <h1>{t('home.title')}</h1>
        <p className="muted">{t('home.subtitle')}</p>
        <form className="search" role="search" onSubmit={submit}>
          <label className="visually-hidden" htmlFor="q">
            {t('home.searchPlaceholder')}
          </label>
          <input id="q" type="search" value={q} placeholder={t('home.searchPlaceholder')} onChange={(e) => setQ(e.target.value)} />
          <button type="submit" className="btn">
            {t('common.search')}
          </button>
        </form>
      </section>

      <section className="filters" aria-label={t('home.filters')}>
        <label className="field">
          <span>{t('home.category')}</span>
          <select value={category} onChange={(e) => update({ category: e.target.value || null, service: null })}>
            <option value="">{t('common.all')}</option>
            {facets.data?.categories.map((c) => (
              <option key={c.key} value={c.key}>
                {c.name}
              </option>
            ))}
          </select>
        </label>
        <label className="field">
          <span>{t('home.service')}</span>
          <select value={params.get('service') ?? ''} onChange={(e) => update({ service: e.target.value || null })}>
            <option value="">{t('common.all')}</option>
            {services.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </select>
        </label>
        <label className="field">
          <span>{t('home.district')}</span>
          <select value={params.get('district') ?? ''} onChange={(e) => update({ district: e.target.value || null })}>
            <option value="">{t('common.all')}</option>
            {facets.data?.districts.map((d) => (
              <option key={d} value={d}>
                {d}
              </option>
            ))}
          </select>
        </label>
        <label className="field">
          <span>{t('home.sort')}</span>
          <select value={effectiveSort} onChange={(e) => chooseSort(e.target.value as ClinicSort)}>
            {sorts.map((s) => (
              <option key={s} value={s}>
                {t(`home.sort.${s}`)}
              </option>
            ))}
          </select>
        </label>
        <div className="field field--check">
          <Checkbox label={t('home.availableSoon')} checked={params.get('soon') === '1'} onChange={(on) => update({ soon: on ? '1' : null })} />
        </div>
      </section>
      <div aria-live="polite" className="small">
        {geoState === 'locating' && <p className="muted">{t('home.locating')}</p>}
        {geoState === 'denied' && <p className="alert alert--warning">{t('home.geoDenied')}</p>}
      </div>

      <section aria-labelledby="results-title" aria-busy={results.loading}>
        <h2 id="results-title" className="visually-hidden">
          {t('home.resultsTitle')}
        </h2>
        {results.error !== undefined ? (
          <ApiErrorState error={results.error} onRetry={results.reload} />
        ) : !results.data ? (
          <LoadingState />
        ) : results.data.items.length === 0 ? (
          <EmptyState title={t('home.empty')} />
        ) : (
          <>
            <p className="muted" aria-live="polite">
              {t('home.results', { count: results.data.totalCount })}
            </p>
            <ul className="card-grid">
              {results.data.items.map((c) => (
                <li key={c.id}>
                  <ClinicCardView clinic={c} />
                </li>
              ))}
            </ul>
            <Pagination page={results.data.page} pageSize={results.data.pageSize} totalCount={results.data.totalCount} onPage={(p) => update({ page: String(p) })} />
          </>
        )}
      </section>
    </div>
  )
}

function ClinicCardView({ clinic }: { clinic: ClinicCard }) {
  const { t, locale } = useI18n()
  return (
    <article className="card clinic-card">
      <h3>
        <Link to={`/clinics/${clinic.id}`}>{clinic.name}</Link>
      </h3>
      <p className="clinic-card__meta">
        {clinic.rating !== null ? (
          <>
            <Stars rating={clinic.rating} label={t('home.ratingLabel', { rating: clinic.rating.toFixed(1) })} />{' '}
            {t('home.rating', { rating: clinic.rating.toFixed(1), count: clinic.reviewCount })}
          </>
        ) : (
          <span className="muted">{t('home.noRating')}</span>
        )}
      </p>
      <p className="muted small">
        {clinic.districts.join(', ')} · {t('home.branches', { count: clinic.branchCount })}
        {clinic.distanceKm !== null && <> · {t('home.distance', { km: clinic.distanceKm.toFixed(1) })}</>}
      </p>
      {clinic.minPrice && <p className="clinic-card__price">{t('home.priceFrom', { price: formatMoney(clinic.minPrice, locale) })}</p>}
      {clinic.nextFreeSlot && (
        <p className="small">{t('home.nextSlot', { time: formatDateTime(clinic.nextFreeSlot, DEFAULT_TIME_ZONE, locale) })}</p>
      )}
      <div className="row">
        <Link className="btn btn--small" to={`/book/${clinic.id}`}>
          {t('home.book')}
        </Link>
        <Link className="btn btn--ghost btn--small" to={`/clinics/${clinic.id}`}>
          {t('common.details')}
        </Link>
      </div>
    </article>
  )
}
