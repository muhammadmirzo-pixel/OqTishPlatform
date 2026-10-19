import { useI18n } from '../i18n/context'

/** Tooth-with-smile mark. Decorative: the word mark next to it carries the name. */
export function LogoMark({ className = 'brand__mark' }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 32 32" fill="none" aria-hidden="true" focusable="false">
      <path
        d="M16 4.5c-2.9 0-4.4 1.3-6.6 1.3S5.6 4.5 4.6 6.6C3 9.8 4.3 13.8 5.6 17c1 2.6 1.4 5.9 2.2 8.7.5 1.9 2.5 2.1 3.2.3.8-2.3 1.3-5.7 3.2-5.7h3.6c1.9 0 2.4 3.4 3.2 5.7.7 1.8 2.7 1.6 3.2-.3.8-2.8 1.2-6.1 2.2-8.7 1.3-3.2 2.6-7.2 1-10.4-1-2.1-2.6-.8-4.8-.8S18.9 4.5 16 4.5Z"
        stroke="currentColor"
        strokeWidth="2.2"
        strokeLinejoin="round"
      />
      <path d="M11.2 11.6c1.3 1.6 2.9 2.4 4.8 2.4s3.5-.8 4.8-2.4" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" />
    </svg>
  )
}

/** Mark + word mark (+ optional tagline). */
export function Logo({ tagline = true }: { tagline?: boolean }) {
  const { t } = useI18n()
  return (
    <>
      <LogoMark />
      <span className="brand__text">
        <span className="brand__name">
          Oq<b>Tish</b>
        </span>
        {tagline && <span className="brand__tagline">{t('brand.tagline')}</span>}
      </span>
    </>
  )
}
