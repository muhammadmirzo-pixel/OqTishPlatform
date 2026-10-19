import { Building2, Search } from 'lucide-react'
import { useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { useApi } from '../../api/context'
import { clinicVerifications, type AdminClinicDto, type ClinicVerification } from '../../api/types'
import { useSession } from '../../auth/session'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { formatInstantDate } from '../../lib/format'
import { DEFAULT_TIME_ZONE } from '../../lib/time'
import { Pagination } from '../../ui/Pagination'
import { SelectField } from '../../ui/Select'
import { ConfirmDialog, ReasonDialog } from '../../ui/ReasonDialog'
import { useToast } from '../../ui/toastContext'
import { EmptyState, QueryView } from '../../ui/states'
import { Badge, PageHeader } from '../../ui/widgets'
import { PlatformPerm } from '../clinic/permissions'

type Action = { kind: 'approve' | 'reject' | 'suspend' | 'reinstate'; clinic: AdminClinicDto }

export function AdminClinicsPage() {
  const { t, locale } = useI18n()
  useTitle(t('adminNav.clinics'))
  const api = useApi()
  const { me } = useSession()
  const perms = me?.platformPermissions ?? []
  const [params, setParams] = useSearchParams()
  const statusParam = params.get('status') as ClinicVerification | null
  const status = statusParam && clinicVerifications.includes(statusParam) ? statusParam : undefined
  const q = params.get('q') ?? ''
  const page = Number(params.get('page')) || 1
  const [search, setSearch] = useState(q)
  const [action, setAction] = useState<Action | null>(null)
  const toast = useToast()
  const list = useQuery(`admin-clinics:${status ?? ''}:${q}:${page}`, () => api.admin.clinics({ status, q: q || undefined, page }))
  const set = (k: string, v: string | null) =>
    setParams((prev) => {
      const n = new URLSearchParams(prev)
      if (v) n.set(k, v)
      else n.delete(k)
      if (k !== 'page') n.delete('page')
      return n
    })
  const verify = perms.includes(PlatformPerm.clinicVerify)
  const suspend = perms.includes(PlatformPerm.accountSuspend)
  const done = (msg: string) => {
    setAction(null)
    toast({ title: msg })
    list.reload()
  }

  return (
    <div className="stack">
      <PageHeader icon={Building2} title={t('adminNav.clinics')} subtitle={t('adm.clinicsIntro')} />
      <form
        className="filters"
        role="search"
        onSubmit={(e) => {
          e.preventDefault()
          set('q', search.trim() || null)
        }}
      >
        <label className="field">
          <span>{t('common.search')}</span>
          <input type="search" value={search} onChange={(e) => setSearch(e.target.value)} />
        </label>
        <SelectField
          label={t('common.status')}
          value={status ?? ''}
          onChange={(v) => set('status', v || null)}
          options={[{ value: '', label: t('common.all') }, ...clinicVerifications.map((s) => ({ value: s, label: t(`verification.${s}`) }))]}
        />
        <button type="submit" className="btn btn--ghost">
          <Search className="icon" aria-hidden="true" />
          {t('common.search')}
        </button>
      </form>
      <QueryView query={list}>
        {(data) =>
          data.items.length === 0 ? (
            <EmptyState icon={Building2} />
          ) : (
            <>
              <div className="table-wrap">
                <table className="table table--stack">
                  <thead>
                    <tr>
                      <th scope="col">{t('common.clinic')}</th>
                      <th scope="col">{t('common.phone')}</th>
                      <th scope="col">{t('common.status')}</th>
                      <th scope="col">{t('common.createdAt')}</th>
                      <th scope="col">{t('common.actions')}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.items.map((c) => (
                      <tr key={c.id}>
                        <td data-label={t('common.clinic')}>
                          <strong>{c.name}</strong>
                          <div className="muted small">
                            {c.legalName && <>{c.legalName} · </>}
                            {t('home.branches', { count: c.branchCount })}
                          </div>
                        </td>
                        <td data-label={t('common.phone')} className="nowrap">
                          {c.phone || '—'}
                        </td>
                        <td data-label={t('common.status')}>
                          <Badge tone={c.verificationStatus === 'Approved' ? 'success' : c.verificationStatus === 'Pending' ? 'warning' : c.verificationStatus === 'Suspended' ? 'danger' : 'neutral'}>{t(`verification.${c.verificationStatus}`)}</Badge>
                          {c.statusReason && (c.verificationStatus === 'Rejected' || c.verificationStatus === 'Suspended') && (
                            <div className="muted small">{t('adm.statusReason', { reason: c.statusReason })}</div>
                          )}
                        </td>
                        <td data-label={t('common.createdAt')}>{formatInstantDate(c.createdAt, DEFAULT_TIME_ZONE, locale)}</td>
                        <td data-label={t('common.actions')}>
                          <div className="row">
                            {/* Status machine (DECISIONS.md BE1b-11): approve = Pending/Rejected → Approved; reject = Pending → Rejected. */}
                            {verify && (c.verificationStatus === 'Pending' || c.verificationStatus === 'Rejected') && (
                              <button type="button" className="btn btn--small" onClick={() => setAction({ kind: 'approve', clinic: c })}>
                                {t('adm.approve')}
                              </button>
                            )}
                            {verify && c.verificationStatus === 'Pending' && (
                              <button type="button" className="btn btn--ghost btn--small" onClick={() => setAction({ kind: 'reject', clinic: c })}>
                                {t('adm.reject')}
                              </button>
                            )}
                            {suspend && c.verificationStatus === 'Approved' && (
                              <button type="button" className="btn btn--ghost btn--small" onClick={() => setAction({ kind: 'suspend', clinic: c })}>
                                {t('adm.suspend')}
                              </button>
                            )}
                            {suspend && c.verificationStatus === 'Suspended' && (
                              <button type="button" className="btn btn--ghost btn--small" onClick={() => setAction({ kind: 'reinstate', clinic: c })}>
                                {t('adm.reinstate')}
                              </button>
                            )}
                            {perms.includes(PlatformPerm.financeSettle) && (
                              <Link className="btn btn--ghost btn--small" to={`/admin/commission?clinic=${c.id}`}>
                                {t('adminNav.commission')}
                              </Link>
                            )}
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <Pagination page={data.page} pageSize={data.pageSize} totalCount={data.totalCount} onPage={(p) => set('page', String(p))} />
            </>
          )
        }
      </QueryView>
      {action && (action.kind === 'approve' || action.kind === 'reinstate') && (
        <ConfirmDialog
          title={`${t(action.kind === 'approve' ? 'adm.approve' : 'adm.reinstate')}: ${action.clinic.name}`}
          body={<p>{t(action.kind === 'approve' ? 'adm.approveBody' : 'adm.reinstateBody')}</p>}
          confirmLabel={t(action.kind === 'approve' ? 'adm.approve' : 'adm.reinstate')}
          onConfirm={() => (action.kind === 'approve' ? api.admin.approveClinic(action.clinic.id) : api.admin.reinstateClinic(action.clinic.id))}
          onDone={() => done(t('adm.done'))}
          onClose={() => setAction(null)}
        />
      )}
      {action && (action.kind === 'reject' || action.kind === 'suspend') && (
        <ReasonDialog
          title={`${t(action.kind === 'reject' ? 'adm.reject' : 'adm.suspend')}: ${action.clinic.name}`}
          label={t('common.reason')}
          danger
          intro={<p>{t('adm.reasonAudited')}</p>}
          submitLabel={t(action.kind === 'reject' ? 'adm.reject' : 'adm.suspend')}
          maxLength={1000}
          onSubmit={(reason) => (action.kind === 'reject' ? api.admin.rejectClinic(action.clinic.id, reason) : api.admin.suspendClinic(action.clinic.id, reason))}
          onDone={() => done(t('adm.done'))}
          onClose={() => setAction(null)}
        />
      )}
    </div>
  )
}
