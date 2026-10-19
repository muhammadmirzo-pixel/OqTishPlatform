import { Search, Users } from 'lucide-react'
import { useState } from 'react'
import { useApi } from '../../api/context'
import type { AdminUserDto } from '../../api/types'
import { useSession } from '../../auth/session'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { formatInstantDate } from '../../lib/format'
import { DEFAULT_TIME_ZONE } from '../../lib/time'
import { Pagination } from '../../ui/Pagination'
import { ConfirmDialog, ReasonDialog } from '../../ui/ReasonDialog'
import { useToast } from '../../ui/toastContext'
import { EmptyState, QueryView } from '../../ui/states'
import { Badge, PageHeader } from '../../ui/widgets'

export function AdminUsersPage() {
  const { t, locale } = useI18n()
  useTitle(t('adminNav.users'))
  const api = useApi()
  const { me } = useSession()
  const [q, setQ] = useState('')
  const [query, setQuery] = useState('')
  const [page, setPage] = useState(1)
  const [action, setAction] = useState<{ kind: 'suspend' | 'reinstate'; user: AdminUserDto } | null>(null)
  const toast = useToast()
  const list = useQuery(`admin-users:${query}:${page}`, () => api.admin.users({ q: query || undefined, page }))
  const done = () => {
    setAction(null)
    toast({ title: t('adm.done') })
    list.reload()
  }

  return (
    <div className="stack">
      <PageHeader icon={Users} title={t('adminNav.users')} subtitle={t('adm.usersIntro')} />
      <form
        className="filters"
        role="search"
        onSubmit={(e) => {
          e.preventDefault()
          setPage(1)
          setQuery(q.trim())
        }}
      >
        <label className="field">
          <span>{t('adm.userSearch')}</span>
          <input type="search" value={q} onChange={(e) => setQ(e.target.value)} />
        </label>
        <button type="submit" className="btn btn--ghost">
          <Search className="icon" aria-hidden="true" />
          {t('common.search')}
        </button>
      </form>
      <QueryView query={list}>
        {(data) =>
          data.items.length === 0 ? (
            <EmptyState icon={Users} />
          ) : (
            <>
              <div className="table-wrap">
                <table className="table table--stack">
                  <thead>
                    <tr>
                      <th scope="col">{t('common.name')}</th>
                      <th scope="col">{t('common.phone')}</th>
                      <th scope="col">{t('common.status')}</th>
                      <th scope="col">{t('common.createdAt')}</th>
                      <th scope="col">{t('common.actions')}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.items.map((u) => (
                      <tr key={u.id}>
                        <td data-label={t('common.name')}>
                          {u.displayName}
                          {u.email && <div className="muted small">{u.email}</div>}
                        </td>
                        <td data-label={t('common.phone')} className="nowrap">
                          {u.phoneMasked ?? '—'}
                        </td>
                        <td data-label={t('common.status')}>
                          <Badge tone={u.status === 'Active' ? 'success' : 'danger'}>{t(`staff.${u.status}`)}</Badge>
                          {u.status === 'Suspended' && u.statusReason && <div className="muted small">{u.statusReason}</div>}
                        </td>
                        <td data-label={t('common.createdAt')}>{formatInstantDate(u.createdAt, DEFAULT_TIME_ZONE, locale)}</td>
                        <td data-label={t('common.actions')}>
                          {u.id === me?.id ? (
                            <span className="muted small">{t('doc.you')}</span>
                          ) : u.status === 'Active' ? (
                            <button type="button" className="btn btn--ghost btn--small" onClick={() => setAction({ kind: 'suspend', user: u })}>
                              {t('adm.suspend')}
                            </button>
                          ) : (
                            <button type="button" className="btn btn--ghost btn--small" onClick={() => setAction({ kind: 'reinstate', user: u })}>
                              {t('adm.reinstate')}
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
      {action?.kind === 'suspend' && (
        <ReasonDialog
          title={`${t('adm.suspend')}: ${action.user.displayName}`}
          label={t('common.reason')}
          danger
          intro={<p>{t('adm.suspendUserBody')}</p>}
          submitLabel={t('adm.suspend')}
          maxLength={1000}
          onSubmit={(reason) => api.admin.suspendUser(action.user.id, reason)}
          onDone={done}
          onClose={() => setAction(null)}
        />
      )}
      {action?.kind === 'reinstate' && (
        <ConfirmDialog
          title={`${t('adm.reinstate')}: ${action.user.displayName}`}
          body={<p>{t('adm.reinstateUserBody')}</p>}
          confirmLabel={t('adm.reinstate')}
          onConfirm={() => api.admin.reinstateUser(action.user.id)}
          onDone={done}
          onClose={() => setAction(null)}
        />
      )}
    </div>
  )
}
