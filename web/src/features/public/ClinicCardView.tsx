import { BadgeCheck, CalendarClock, MapPin, Navigation, Star } from 'lucide-react'
import { Link } from 'react-router'
import type { ClinicCard } from '../../api/types'
import { useI18n } from '../../i18n/context'
import { formatDateTime, formatMoney } from '../../lib/format'
import { DEFAULT_TIME_ZONE } from '../../lib/time'
import { Monogram } from '../../ui/Monogram'

/**
 * Catalogue card. Every figure comes from the API: rating only when published reviews exist,
 * distance only when the visitor shared a location, next free slot only when the server found one.
 * `layout="row"` is the catalogue list row (media | details | price and actions) from 900 px up;
 * on narrower screens it falls back to the card arrangement.
 */
export function ClinicCardView({ clinic, layout = 'card' }: { clinic: ClinicCard; layout?: 'card' | 'row' }) {
  const { t, locale } = useI18n()

  const title = (
    <div className="clinic-card__title">
      <h3>
        <Link to={`/clinics/${clinic.id}`}>{clinic.name}</Link>
      </h3>
      <span className="badge badge--success">
        <BadgeCheck className="icon" aria-hidden="true" />
        {t('catalog.verified')}
      </span>
    </div>
  )
  const meta = (
    <p className="clinic-card__meta">
      {clinic.rating !== null ? (
        <span className="rating" role="img" aria-label={t('home.ratingLabel', { rating: clinic.rating.toFixed(1) })}>
          <Star className="icon" aria-hidden="true" />
          {clinic.rating.toFixed(1)} <span className="rating__count">({clinic.reviewCount})</span>
        </span>
      ) : (
        <span>{t('home.noRating')}</span>
      )}
    </p>
  )
  const facts = (
    <ul className="clinic-card__facts">
      <li>
        <MapPin className="icon" aria-hidden="true" />
        {clinic.districts.join(', ')} · {t('home.branches', { count: clinic.branchCount })}
      </li>
      {clinic.distanceKm !== null && (
        <li>
          <Navigation className="icon" aria-hidden="true" />
          {t('home.distance', { km: clinic.distanceKm.toFixed(1) })}
        </li>
      )}
      {clinic.nextFreeSlot && (
        <li>
          <CalendarClock className="icon" aria-hidden="true" />
          {t('catalog.nextSlot')}: {formatDateTime(clinic.nextFreeSlot, DEFAULT_TIME_ZONE, locale)}
        </li>
      )}
    </ul>
  )
  const foot = (
    <div className="clinic-card__foot">
      {clinic.minPrice ? <p className="clinic-card__price">{t('home.priceFrom', { price: formatMoney(clinic.minPrice, locale) })}</p> : <span />}
      <div className="row">
        <Link className="btn btn--ghost btn--small" to={`/clinics/${clinic.id}`}>
          {t('common.details')}
        </Link>
        <Link className="btn btn--small" to={`/book/${clinic.id}`}>
          {t('home.book')}
        </Link>
      </div>
    </div>
  )

  if (layout === 'row') {
    return (
      <article className="card clinic-card clinic-card--row">
        <div className="clinic-card__media">
          <Monogram name={clinic.name} seed={clinic.id} imageId={clinic.logoId} size={56} />
        </div>
        <div className="clinic-card__body">
          {title}
          {meta}
          {facts}
        </div>
        {foot}
      </article>
    )
  }

  return (
    <article className="card clinic-card">
      <div className="clinic-card__head">
        <Monogram name={clinic.name} seed={clinic.id} imageId={clinic.logoId} size={56} />
        {title}
      </div>
      {meta}
      {facts}
      {foot}
    </article>
  )
}
