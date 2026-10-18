import { useState } from 'react'
import { useApi } from '../../api/context'
import type { AdminUserDto } from '../../api/types'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { formatInstantDate } from '../../lib/format'
import { DEFAULT_TIME_ZONE } from '../../lib/time'
import { Notice } from '../../ui/forms'
import { Pagination } from '../../ui/Pagination'
import { ConfirmDialog, ReasonDialog } from '../../ui/ReasonDialog'
import { EmptyState, QueryView } from '../../ui/states'
import { Badge, PageHeader } from '../../ui/widgets'

export function AdminUsersPage() {
  const { t, locale } = useI18n()
  useTitle(t('adminNav.users'))
  const api = useApi()
  const [q, setQ] = useState('')
  const [query, setQuery] = useState('')
  const [page, setPage] = useState(1)
  const [action, setAction] = useState<{ kind: 'suspend' | 'reinstate'; user: AdminUserDto } | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const list = useQuery(`admin-users:${query}:${page}`, () => api.admin.users({ q: query || undefined, page }))
  const done = () => {
    setAction(null)
    setNotice(t('adm.done'))
    list.reload()
  }

  return (
    <div className="stack">
      <PageHeader title={t('adminNav.users')} subtitle={t('adm.usersIntro')} />
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
          {t('common.search')}
        </button>
      </form>
      <div aria-live="polite">{notice && <Notice>{notice}</Notice>}</div>
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
                        <td>
                          {u.displayName}
                          {u.email && <div className="muted small">{u.email}</div>}
                        </td>
                        <td>{u.phoneMasked ?? '—'}</td>
                        <td>
                          <Badge tone={u.status === 'Active' ? 'success' : 'danger'}>{t(`staff.${u.status}`)}</Badge>
                        </td>
                        <td>{formatInstantDate(u.createdAt, DEFAULT_TIME_ZONE, locale)}</td>
                        <td>
                          {u.status === 'Active' ? (
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
          intro={<p className="muted">{t('adm.suspendUserBody')}</p>}
          submitLabel={t('adm.suspend')}
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
