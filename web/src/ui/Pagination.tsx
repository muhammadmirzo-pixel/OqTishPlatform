import { useI18n } from '../i18n/context'

export function Pagination({ page, pageSize, totalCount, onPage }: { page: number; pageSize: number; totalCount: number; onPage: (page: number) => void }) {
  const { t } = useI18n()
  const pages = Math.max(1, Math.ceil(totalCount / Math.max(1, pageSize)))
  if (pages <= 1) return null
  return (
    <nav className="pagination" aria-label={t('page.nav')}>
      <button type="button" className="btn btn--ghost btn--small" disabled={page <= 1} onClick={() => onPage(page - 1)}>
        {t('page.prev')}
      </button>
      <span aria-live="polite">{t('page.info', { page, pages })}</span>
      <button type="button" className="btn btn--ghost btn--small" disabled={page >= pages} onClick={() => onPage(page + 1)}>
        {t('page.next')}
      </button>
    </nav>
  )
}
