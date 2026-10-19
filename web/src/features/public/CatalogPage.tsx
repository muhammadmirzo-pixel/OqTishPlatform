import { Navigation, Search, SlidersHorizontal, X } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { useSearchParams } from 'react-router'
import { useApi } from '../../api/context'
import type { ClinicSearchParams, ClinicSort, Facets } from '../../api/types'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { categoryLabel } from '../../lib/categories'
import { Drawer } from '../../ui/Drawer'
import { SelectField } from '../../ui/Select'
import { Checkbox } from '../../ui/forms'
import { Pagination } from '../../ui/Pagination'
import { SkeletonCards } from '../../ui/Skeleton'
import { ApiErrorState, EmptyState } from '../../ui/states'
import { ClinicCardView } from './ClinicCardView'

const sorts: ClinicSort[] = ['rating', 'price', 'distance', 'name']
const PAGE_SIZE = 12

type Coords = { lat: number; lng: number }
type Update = (changes: Record<string, string | null>) => void

/** Clinic search (/clinics). Filters live in the URL so results can be shared and restored. */
export function CatalogPage() {
  const { t } = useI18n()
  useTitle(t('catalog.title'))
  const api = useApi()
  const [params, setParams] = useSearchParams()
  const [coords, setCoords] = useState<Coords | null>(null)
  const [geoState, setGeoState] = useState<'idle' | 'locating' | 'denied'>('idle')
  const [q, setQ] = useState(params.get('q') ?? '')
  const [filtersOpen, setFiltersOpen] = useState(false)

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

  const update: Update = (changes) => {
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
  const serviceName = facets.data?.services.find((s) => s.id === params.get('service'))?.name
  const active: { key: string; label: string }[] = [
    ...(params.get('q') ? [{ key: 'q', label: `“${params.get('q')}”` }] : []),
    ...(category ? [{ key: 'category', label: categoryLabel(t, category) }] : []),
    ...(params.get('service') ? [{ key: 'service', label: serviceName ?? t('home.service') }] : []),
    ...(params.get('district') ? [{ key: 'district', label: params.get('district') ?? '' }] : []),
    ...(params.get('soon') === '1' ? [{ key: 'soon', label: t('home.availableSoon') }] : []),
  ]
  const resetAll = () => {
    setQ('')
    update({ q: null, category: null, service: null, district: null, soon: null })
  }

  return (
    <div className="page catalog">
      <header className="catalog__head">
        <h1>{t('catalog.title')}</h1>
        <form className="catalog__search" role="search" onSubmit={submit}>
          <label className="input-icon">
            <Search className="icon" aria-hidden="true" />
            <span className="visually-hidden">{t('home.searchPlaceholder')}</span>
            <input id="q" type="search" value={q} placeholder={t('home.searchPlaceholder')} onChange={(e) => setQ(e.target.value)} />
          </label>
          <button type="submit" className="btn">
            {t('common.search')}
          </button>
        </form>
      </header>

      <div className="catalog__toolbar">
        <button type="button" className="btn btn--ghost btn--small catalog__filters-btn" onClick={() => setFiltersOpen(true)}>
          <SlidersHorizontal className="icon" aria-hidden="true" />
          {t('catalog.filters')}
          {active.length > 0 && <span className="badge badge--primary">{active.length}</span>}
        </button>
        <div className="catalog__sorts" role="group" aria-label={t('home.sort')}>
          {sorts.map((s) => (
            <button key={s} type="button" className="chip" aria-pressed={effectiveSort === s} onClick={() => chooseSort(s)}>
              {s === 'distance' && <Navigation className="icon" aria-hidden="true" />}
              {t(`home.sort.${s}`)}
            </button>
          ))}
        </div>
      </div>

      {active.length > 0 && (
        <div className="row catalog__active">
          {active.map((f) => (
            <button key={f.key} type="button" className="chip chip--removable" aria-label={t('catalog.removeFilter', { name: f.label })} onClick={() => (f.key === 'category' ? update({ category: null, service: null }) : update({ [f.key]: null }))}>
              {f.label}
              <X className="icon" aria-hidden="true" />
            </button>
          ))}
          <button type="button" className="btn btn--ghost btn--small" onClick={resetAll}>
            {t('catalog.reset')}
          </button>
        </div>
      )}

      <div aria-live="polite" className="small">
        {geoState === 'locating' && <p className="muted">{t('home.locating')}</p>}
        {geoState === 'denied' && <p className="alert alert--warning">{t('home.geoDenied')}</p>}
      </div>

      <div className="catalog__body">
        <aside className="catalog__aside card" aria-label={t('home.filters')}>
          <Filters facets={facets.data} params={params} update={update} />
        </aside>

        <section aria-labelledby="results-title" aria-busy={results.loading} className="catalog__results">
          <h2 id="results-title" className="visually-hidden">
            {t('home.resultsTitle')}
          </h2>
          {results.error !== undefined ? (
            <ApiErrorState error={results.error} onRetry={results.reload} />
          ) : !results.data ? (
            <SkeletonCards count={6} label={t('catalog.loading')} />
          ) : results.data.items.length === 0 ? (
            <EmptyState title={t('home.empty')} icon={Search}>
              <p>{t('catalog.emptyBody')}</p>
              {active.length > 0 && (
                <button type="button" className="btn" onClick={resetAll}>
                  {t('catalog.reset')}
                </button>
              )}
            </EmptyState>
          ) : (
            <div className="stack">
              <p className="muted" aria-live="polite">
                {t('home.results', { count: results.data.totalCount })}
              </p>
              <ul className="clinic-list">
                {results.data.items.map((c) => (
                  <li key={c.id}>
                    <ClinicCardView clinic={c} layout="row" />
                  </li>
                ))}
              </ul>
              <p className="muted small">{t('catalog.slotNote')}</p>
              <Pagination page={results.data.page} pageSize={results.data.pageSize} totalCount={results.data.totalCount} onPage={(p) => update({ page: String(p) })} />
            </div>
          )}
        </section>
      </div>

      {filtersOpen && (
        <Drawer
          title={t('catalog.filters')}
          onClose={() => setFiltersOpen(false)}
          footer={
            <>
              <button type="button" className="btn btn--ghost" onClick={resetAll}>
                {t('catalog.reset')}
              </button>
              <button type="button" className="btn" onClick={() => setFiltersOpen(false)}>
                {t('catalog.showResults')}
              </button>
            </>
          }
        >
          <Filters facets={facets.data} params={params} update={update} />
        </Drawer>
      )}
    </div>
  )
}

function Filters({ facets, params, update }: { facets: Facets | undefined; params: URLSearchParams; update: Update }) {
  const { t } = useI18n()
  const category = params.get('category') ?? ''
  const services = (facets?.services ?? []).filter((s) => !category || s.category === category)
  return (
    <div className="stack">
      <SelectField
        label={t('home.category')}
        value={category}
        onChange={(v) => update({ category: v || null, service: null })}
        options={[{ value: '', label: t('common.all') }, ...(facets?.categories ?? []).map((c) => ({ value: c.key, label: categoryLabel(t, c.key, c.name) }))]}
      />
      <SelectField
        label={t('home.service')}
        value={params.get('service') ?? ''}
        onChange={(v) => update({ service: v || null })}
        options={[{ value: '', label: t('common.all') }, ...services.map((s) => ({ value: s.id, label: s.name }))]}
      />
      <SelectField
        label={t('home.district')}
        value={params.get('district') ?? ''}
        onChange={(v) => update({ district: v || null })}
        options={[{ value: '', label: t('common.all') }, ...(facets?.districts ?? []).map((d) => ({ value: d, label: d }))]}
      />
      <Checkbox label={t('home.availableSoon')} checked={params.get('soon') === '1'} onChange={(on) => update({ soon: on ? '1' : null })} />
    </div>
  )
}
