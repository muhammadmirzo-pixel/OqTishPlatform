import { useMemo } from 'react'
import { Link, Outlet, useNavigate, useParams } from 'react-router'
import { useApi } from '../api/context'
import { findMembership, useSession } from '../auth/session'
import { ClinicContext, makeClinicContext } from '../features/clinic/clinicContext'
import { useBranchParam } from '../features/clinic/branchParam'
import { clinicMenu, roleNeedsTotp, visibleMenu } from '../features/clinic/permissions'
import { useQuery } from '../hooks/useQuery'
import { useI18n } from '../i18n/context'
import { ApiErrorState, ForbiddenState, LoadingState, TotpRequiredState } from '../ui/states'
import { ConsoleShell } from './ConsoleShell'

export function ClinicLayout() {
  const { clinicId } = useParams()
  const { me } = useSession()
  const { t } = useI18n()
  const api = useApi()
  const navigate = useNavigate()
  const membership = findMembership(me, clinicId)
  const [branch, setBranch] = useBranchParam()
  const branchesQuery = useQuery(membership && !membership.totpSetupRequired ? `branches:${membership.clinicId}` : null, () => api.clinic.branches(membership!.clinicId))

  const ctx = useMemo(
    () => (membership && branchesQuery.data ? makeClinicContext(membership, branchesQuery.data, branchesQuery.reload) : null),
    [membership, branchesQuery.data, branchesQuery.reload],
  )

  if (!me) return <LoadingState />
  if (!membership) {
    return (
      <div className="page narrow">
        <ForbiddenState>
          <Link className="btn" to="/clinic">
            {t('console.myClinics')}
          </Link>
        </ForbiddenState>
      </div>
    )
  }

  const items = visibleMenu(clinicMenu, membership.permissions)
  const base = `/clinic/${membership.clinicId}`
  const banner = (
    <>
      {(membership.totpSetupRequired || (roleNeedsTotp(membership.role) && !me.totpEnabled)) && (
        <div className="banner banner--warning" role="note">
          {t('console.totpBanner')} <Link to="/me/security">{t('state.totpLink')}</Link>
        </div>
      )}
      {membership.clinicStatus !== 'Approved' && (
        <div className="banner banner--info" role="note">
          {t(`console.clinicStatus.${membership.clinicStatus}`)}
        </div>
      )}
    </>
  )

  const topbar = (
    <>
      {me.memberships.length > 1 && (
        <label className="field field--inline">
          <span>{t('common.clinic')}</span>
          <select value={membership.clinicId} onChange={(e) => navigate(`/clinic/${e.target.value}`)}>
            {me.memberships.map((m) => (
              <option key={m.clinicId} value={m.clinicId}>
                {m.clinicName}
              </option>
            ))}
          </select>
        </label>
      )}
      {ctx && ctx.branches.length > 0 && (
        <label className="field field--inline">
          <span>{t('common.branch')}</span>
          <select value={branch ?? ''} onChange={(e) => setBranch(e.target.value || undefined)}>
            <option value="">{t('common.allBranches')}</option>
            {ctx.branches.map((b) => (
              <option key={b.id} value={b.id}>
                {b.name}
              </option>
            ))}
          </select>
        </label>
      )}
    </>
  )

  return (
    <ConsoleShell
      title={membership.clinicName}
      subtitle={<p className="muted small console__role">{t(`role.${membership.role}`)}</p>}
      base={base}
      items={items}
      linkSuffix={branch ? `?branch=${encodeURIComponent(branch)}` : ''}
      topbar={topbar}
      banner={banner}
    >
      {membership.totpSetupRequired ? (
        <TotpRequiredState />
      ) : ctx ? (
        <ClinicContext.Provider value={ctx}>
          <Outlet />
        </ClinicContext.Provider>
      ) : branchesQuery.error !== undefined ? (
        <ApiErrorState error={branchesQuery.error} onRetry={branchesQuery.reload} />
      ) : (
        <LoadingState />
      )}
    </ConsoleShell>
  )
}
