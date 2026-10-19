import { ScrollText } from 'lucide-react'
import type { AuditEntryDto, Page } from '../../api/types'
import { useI18n } from '../../i18n/context'
import { formatFullDateTime } from '../../lib/format'
import { DEFAULT_TIME_ZONE } from '../../lib/time'
import { Pagination } from '../../ui/Pagination'
import { EmptyState } from '../../ui/states'

/** Read-only audit trail (append-only on the server). Times shown in Asia/Tashkent. */
export function AuditTable({ page, onPage, showClinic }: { page: Page<AuditEntryDto>; onPage: (p: number) => void; showClinic?: boolean }) {
  const { t, locale } = useI18n()
  if (page.items.length === 0) return <EmptyState title={t('audit.none')} icon={ScrollText} />
  return (
    <>
      <div className="table-wrap">
        <table className="table table--stack">
          <thead>
            <tr>
              <th scope="col">{t('audit.when')}</th>
              <th scope="col">{t('audit.actor')}</th>
              <th scope="col">{t('audit.action')}</th>
              <th scope="col">{t('audit.target')}</th>
              {showClinic && <th scope="col">{t('common.clinic')}</th>}
            </tr>
          </thead>
          <tbody>
            {page.items.map((e) => (
              <tr key={e.id}>
                <td data-label={t('audit.when')}>{formatFullDateTime(e.occurredAt, DEFAULT_TIME_ZONE, locale)}</td>
                <td data-label={t('audit.actor')}>
                  {e.actorKind === 'User' ? (e.actorName ?? t('audit.unknownUser')) : t(`actor.${e.actorKind}`)}
                  {e.actorKind === 'User' && e.actorUserId && (
                    <div className="muted small" title={e.actorUserId}>
                      {e.actorUserId.slice(0, 8)}
                    </div>
                  )}
                </td>
                <td data-label={t('audit.action')}>
                  <code>{e.action}</code>
                </td>
                <td className="small" data-label={t('audit.target')}>
                  {e.targetType ? `${e.targetType} ${e.targetId ?? ''}` : '—'}
                </td>
                {showClinic && (
                  <td className="small" data-label={t('common.clinic')}>
                    {e.clinicId ?? '—'}
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <Pagination page={page.page} pageSize={page.pageSize} totalCount={page.totalCount} onPage={onPage} />
      <p className="muted small">{t('audit.tzNote')}</p>
    </>
  )
}
