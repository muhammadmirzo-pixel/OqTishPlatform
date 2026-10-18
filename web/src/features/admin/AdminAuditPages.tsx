import { useState } from 'react'
import { useApi } from '../../api/context'
import { notificationStatuses, type NotificationStatus } from '../../api/types'
import { useAction } from '../../hooks/useAction'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { formatFullDateTime } from '../../lib/format'
import { DEFAULT_TIME_ZONE } from '../../lib/time'
import { FormError } from '../../ui/forms'
import { Pagination } from '../../ui/Pagination'
import { EmptyState, QueryView } from '../../ui/states'
import { Badge, PageHeader } from '../../ui/widgets'
import { AuditTable } from '../finance/AuditTable'

export function AdminAuditPage() {
  const { t } = useI18n()
  useTitle(t('adminNav.audit'))
  const api = useApi()
  const [clinicId, setClinicId] = useState('')
  const [action, setAction] = useState('')
  const [applied, setApplied] = useState({ clinicId: '', action: '' })
  const [page, setPage] = useState(1)
  const audit = useQuery(`admin-audit:${applied.clinicId}:${applied.action}:${page}`, () =>
    api.admin.audit({ clinicId: applied.clinicId || undefined, action: applied.action || undefined, page }),
  )
  return (
    <div className="stack">
      <PageHeader title={t('adminNav.audit')} />
      <form
        className="filters"
        onSubmit={(e) => {
          e.preventDefault()
          setPage(1)
          setApplied({ clinicId: clinicId.trim(), action: action.trim() })
        }}
      >
        <label className="field">
          <span>{t('adm.clinicId')}</span>
          <input value={clinicId} onChange={(e) => setClinicId(e.target.value)} />
        </label>
        <label className="field">
          <span>{t('audit.action')}</span>
          <input value={action} placeholder="clinic.approved" onChange={(e) => setAction(e.target.value)} />
        </label>
        <button type="submit" className="btn btn--ghost">
          {t('common.search')}
        </button>
      </form>
      <QueryView query={audit}>{(p) => <AuditTable page={p} onPage={setPage} showClinic />}</QueryView>
    </div>
  )
}

const tone: Record<NotificationStatus, string> = { Pending: 'warning', Sent: 'success', Failed: 'danger' }

export function NotificationsPage() {
  const { t, locale } = useI18n()
  useTitle(t('adminNav.notifications'))
  const api = useApi()
  const [status, setStatus] = useState<NotificationStatus | ''>('Failed')
  const [page, setPage] = useState(1)
  const list = useQuery(`admin-notifications:${status}:${page}`, () => api.admin.notifications({ status: status || undefined, page }))
  const retry = useAction((id: string) => api.admin.retryNotification(id))
  return (
    <div className="stack">
      <PageHeader title={t('adminNav.notifications')} subtitle={t('adm.outboxIntro')} />
      <label className="field field--inline">
        <span>{t('common.status')}</span>
        <select
          value={status}
          onChange={(e) => {
            setStatus(e.target.value as NotificationStatus | '')
            setPage(1)
          }}
        >
          <option value="">{t('common.all')}</option>
          {notificationStatuses.map((s) => (
            <option key={s} value={s}>
              {t(`adm.outbox.${s}`)}
            </option>
          ))}
        </select>
      </label>
      <FormError error={retry.error} />
      <QueryView query={list}>
        {(data) =>
          data.items.length === 0 ? (
            <EmptyState />
          ) : (
            <>
              <div className="table-wrap">
                <table className="table">
                  <thead>
                    <tr>
                      <th scope="col">{t('common.createdAt')}</th>
                      <th scope="col">{t('adm.outboxType')}</th>
                      <th scope="col">{t('common.status')}</th>
                      <th scope="col">{t('adm.attempts')}</th>
                      <th scope="col">{t('adm.lastError')}</th>
                      <th scope="col">{t('common.actions')}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.items.map((n) => (
                      <tr key={n.id}>
                        <td>{formatFullDateTime(n.createdAt, DEFAULT_TIME_ZONE, locale)}</td>
                        <td>
                          <code>{n.type}</code>
                          <div className="muted small">{n.channel}</div>
                        </td>
                        <td>
                          <Badge tone={tone[n.status]}>{t(`adm.outbox.${n.status}`)}</Badge>
                        </td>
                        <td>{n.attempts}</td>
                        <td className="small">{n.lastError ?? '—'}</td>
                        <td>
                          {n.status === 'Failed' && (
                            <button
                              type="button"
                              className="btn btn--ghost btn--small"
                              disabled={retry.pending}
                              onClick={async () => {
                                const r = await retry.run(n.id)
                                if (r.ok) list.reload()
                              }}
                            >
                              {t('common.retry')}
                            </button>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <Pagination page={data.page} pageSize={data.pageSize} totalCount={data.totalCount} onPage={setPage} />
            </>
          )
        }
      </QueryView>
    </div>
  )
}
