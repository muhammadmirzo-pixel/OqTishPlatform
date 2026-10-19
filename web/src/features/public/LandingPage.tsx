import { ArrowRight, BadgeCheck, CalendarCheck, CalendarClock, ChevronDown, Hourglass, LockKeyhole, MapPin, Search, ShieldCheck, Smile, Tag, Wallet } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router'
import { useApi } from '../../api/context'
import { useSession } from '../../auth/session'
import { serviceCategories } from '../../api/types'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { categoryLabel } from '../../lib/categories'
import { LogoMark } from '../../ui/Logo'
import { Select } from '../../ui/Select'
import { SkeletonCards } from '../../ui/Skeleton'
import { categoryIcon, categoryTone } from './categoryIcons'
import { ClinicCardView } from './ClinicCardView'

export function LandingPage() {
  const { t } = useI18n()
  useTitle(t('landing.title'))
  const api = useApi()
  const { me } = useSession()
  const navigate = useNavigate()
  const facets = useQuery('facets', () => api.public.facets())
  const clinics = useQuery('landing:clinics', () => api.public.searchClinics({ sort: 'rating', page: 1, pageSize: 3 }))
  const [q, setQ] = useState('')
  const [district, setDistrict] = useState('')
  const [category, setCategory] = useState('')

  function submit(e: FormEvent) {
    e.preventDefault()
    const params = new URLSearchParams()
    if (q.trim()) params.set('q', q.trim())
    if (district) params.set('district', district)
    if (category) params.set('category', category)
    const qs = params.toString()
    navigate(`/clinics${qs ? `?${qs}` : ''}`)
  }

  const features = [
    { icon: MapPin, title: t('landing.feature.near'), body: t('landing.feature.nearBody') },
    { icon: CalendarClock, title: t('landing.feature.online'), body: t('landing.feature.onlineBody') },
    { icon: BadgeCheck, title: t('landing.feature.verified'), body: t('landing.feature.verifiedBody') },
    { icon: Tag, title: t('landing.feature.price'), body: t('landing.feature.priceBody') },
  ]
  const steps = [1, 2, 3, 4] as const
  const trust = [
    { icon: ShieldCheck, n: 1 },
    { icon: CalendarCheck, n: 2 },
    { icon: Wallet, n: 3 },
    { icon: LockKeyhole, n: 4 },
  ] as const
  const faqs = [1, 2, 3, 4, 5] as const

  return (
    <div className="landing">
      <section className="lp-hero">
        <div className="lp-hero__inner">
          <div className="lp-hero__copy">
            {me?.displayName && <p className="lp-hero__greeting">{t('landing.greeting', { name: me.displayName })}</p>}
            <h1 className="lp-hero__title">
              {t('landing.title')} <span>{t('landing.titleAccent')}</span>
            </h1>
            <p className="lp-hero__lead">{t('landing.subtitle')}</p>
            <form className="lp-search" role="search" onSubmit={submit} aria-label={t('common.search')}>
              <label className="lp-search__field lp-search__field--wide">
                <Search className="icon" aria-hidden="true" />
                <span className="lp-search__label">{t('landing.searchQuery')}</span>
                <input type="search" value={q} onChange={(e) => setQ(e.target.value)} placeholder={t('landing.searchQuery')} />
              </label>
              <div className="lp-search__field">
                <MapPin className="icon" aria-hidden="true" />
                <label className="lp-search__label" id="lp-district-label" htmlFor="lp-district">
                  {t('landing.searchDistrict')}
                </label>
                <Select
                  id="lp-district"
                  variant="bare"
                  value={district}
                  onChange={setDistrict}
                  options={[{ value: '', label: t('landing.searchAnyDistrict') }, ...(facets.data?.districts ?? []).map((d) => ({ value: d, label: d }))]}
                />
              </div>
              <div className="lp-search__field">
                <CalendarCheck className="icon" aria-hidden="true" />
                <label className="lp-search__label" id="lp-category-label" htmlFor="lp-category">
                  {t('landing.searchCategory')}
                </label>
                <Select
                  id="lp-category"
                  variant="bare"
                  value={category}
                  onChange={setCategory}
                  options={[{ value: '', label: t('landing.searchAnyCategory') }, ...serviceCategories.map((c) => ({ value: c, label: categoryLabel(t, c) }))]}
                />
              </div>
              <button type="submit" className="btn btn--large lp-search__submit">
                <Search className="icon" aria-hidden="true" />
                {t('landing.searchSubmit')}
              </button>
            </form>
            <ul className="lp-features">
              {features.map(({ icon: Icon, title, body }) => (
                <li key={title}>
                  <span className="icon-tile icon-tile--sm">
                    <Icon className="icon" aria-hidden="true" />
                  </span>
                  <span>
                    <strong>{title}</strong>
                    <small>{body}</small>
                  </span>
                </li>
              ))}
            </ul>
          </div>
          <HeroArt />
        </div>
      </section>

      <div className="page lp-sections">
        <section className="stack" aria-labelledby="lp-cat">
          <div className="section-head">
            <div>
              <h2 id="lp-cat">{t('landing.categoriesTitle')}</h2>
              <p className="muted">{t('landing.categoriesLead')}</p>
            </div>
            <Link to="/clinics" className="btn btn--ghost btn--small">
              {t('landing.allClinics')}
              <ArrowRight className="icon" aria-hidden="true" />
            </Link>
          </div>
          <ul className="lp-categories">
            {serviceCategories.map((c) => {
              const Icon = categoryIcon[c]
              const tone = categoryTone[c]
              return (
                <li key={c}>
                  <Link to={`/clinics?category=${c}`} className="lp-category">
                    <span className={`icon-tile${tone ? ` icon-tile--${tone}` : ''}`}>
                      <Icon className="icon" aria-hidden="true" />
                    </span>
                    <span>{categoryLabel(t, c)}</span>
                  </Link>
                </li>
              )
            })}
          </ul>
        </section>

        <section className="lp-how" aria-labelledby="lp-how">
          <h2 id="lp-how">{t('landing.howTitle')}</h2>
          <ol className="lp-steps">
            {steps.map((n) => (
              <li key={n}>
                <span className="lp-steps__num">{n}</span>
                <h3>{t(`landing.how${n}`)}</h3>
                <p className="muted">{t(`landing.how${n}Body`)}</p>
              </li>
            ))}
          </ol>
        </section>

        {(clinics.loading || (clinics.data && clinics.data.items.length > 0)) && (
          <section className="stack lp-clinics" aria-labelledby="lp-clinics">
            <div className="section-head">
              <div>
                <h2 id="lp-clinics">{t('landing.clinicsTitle')}</h2>
                <p className="muted">{t('landing.clinicsLead')}</p>
              </div>
              <Link to="/clinics" className="btn btn--ghost btn--small">
                {t('landing.allClinics')}
                <ArrowRight className="icon" aria-hidden="true" />
              </Link>
            </div>
            {clinics.data ? (
              <ul className="card-grid">
                {clinics.data.items.map((c) => (
                  <li key={c.id}>
                    <ClinicCardView clinic={c} />
                  </li>
                ))}
              </ul>
            ) : (
              <SkeletonCards count={3} label={t('catalog.loading')} />
            )}
          </section>
        )}

        <section className="stack" aria-labelledby="lp-trust">
          <h2 id="lp-trust">{t('landing.trustTitle')}</h2>
          <ul className="lp-trust">
            {trust.map(({ icon: Icon, n }) => (
              <li key={n} className="card">
                <span className="icon-tile">
                  <Icon className="icon" aria-hidden="true" />
                </span>
                <h3>{t(`landing.trust${n}`)}</h3>
                <p className="muted">{t(`landing.trust${n}Body`)}</p>
              </li>
            ))}
          </ul>
        </section>

        <section className="lp-faq" aria-labelledby="lp-faq">
          <h2 id="lp-faq">{t('landing.faqTitle')}</h2>
          <div className="lp-faq__list">
            {faqs.map((n) => (
              <details key={n} className="lp-faq__item">
                <summary>
                  {t(`landing.faq${n}q`)}
                  <ChevronDown className="icon" aria-hidden="true" />
                </summary>
                <p>{t(`landing.faq${n}a`)}</p>
              </details>
            ))}
          </div>
        </section>

        <section className="lp-cta">
          <div>
            <h2>{t('landing.ctaTitle')}</h2>
            <p>{t('landing.ctaBody')}</p>
          </div>
          <Link to="/clinic/register" className="btn btn--large lp-cta__btn">
            {t('landing.ctaButton')}
            <ArrowRight className="icon" aria-hidden="true" />
          </Link>
        </section>
      </div>
    </div>
  )
}

/** Illustration of the request flow (no real data: statuses only, no prices or names). */
function HeroArt() {
  const { t } = useI18n()
  return (
    <div className="lp-art" aria-hidden="true">
      <div className="lp-art__glow" />
      <div className="lp-art__disc">
        <LogoMark className="lp-art__tooth" />
      </div>
      <div className="lp-art__card lp-art__card--one">
        <span className="icon-tile icon-tile--warning icon-tile--sm">
          <Hourglass className="icon" />
        </span>
        <span>
          <strong>{t('landing.art.requestSent')}</strong>
          <small>{t('landing.art.waiting')}</small>
        </span>
      </div>
      <div className="lp-art__card lp-art__card--two">
        <span className="icon-tile icon-tile--teal icon-tile--sm">
          <CalendarCheck className="icon" />
        </span>
        <span>
          <strong>{t('landing.art.confirmed')}</strong>
          <small>{t('landing.art.doctor')}</small>
        </span>
      </div>
      <div className="lp-art__badge">
        <Smile className="icon lp-art__smile" />
        {t('landing.art.badge')}
      </div>
    </div>
  )
}
