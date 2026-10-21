import { Building2, Store } from 'lucide-react'
import { useMemo } from 'react'
import { Link, Outlet, useNavigate, useParams } from 'react-router'
import { useApi } from '../api/context'
import { findMembership, useSession } from '../auth/session'
import { ClinicContext, makeClinicContext } from '../features/clinic/clinicContext'
import { useBranchParam } from '../features/clinic/branchParam'
import { clinicMenu, Perm, roleNeedsTotp, visibleMenu } from '../features/clinic/permissions'
import { useQuery } from '../hooks/useQuery'
import { useI18n } from '../i18n/context'
import { ContextChip } from '../ui/ContextChip'
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

  // Waiting-request count for the sidebar badge (same figure as the dashboard; refreshed every minute).
  const canTriage = !!membership && !membership.totpSetupRequired && membership.permissions.includes(Perm.triage)
  const pending = useQuery(canTriage ? `nav-pending:${membership!.clinicId}:${branch ?? ''}` : null, () => api.clinic.dashboard(membership!.clinicId, branch), { refreshMs: 60000 })

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
          {membership.clinicStatus === 'Rejected' && (
            <>
              {' '}
              <Link to={`${base}/profile`}>{t('console.seeReason')}</Link>
            </>
          )}
        </div>
      )}
    </>
  )

  const topbar = (
    <>
      {me.memberships.length > 1 && (
        <ContextChip
          icon={Building2}
          label={t('common.clinic')}
          value={membership.clinicId}
          options={me.memberships.map((m) => ({ value: m.clinicId, label: m.clinicName }))}
          onChange={(id) => navigate(`/clinic/${id}`)}
        />
      )}
      {ctx && ctx.branches.length > 0 && (
        // Every branch-dependent query key includes the branch, so the previous branch's data is never shown after a switch.
        <ContextChip
          icon={Store}
          label={t('common.branch')}
          value={branch ?? ''}
          options={[{ value: '', label: t('common.allBranches') }, ...ctx.branches.map((b) => ({ value: b.id, label: b.name }))]}
          onChange={(id) => setBranch(id || undefined)}
        />
      )}
    </>
  )

  return (
    <ConsoleShell
      title={membership.clinicName}
      userRole={t(`role.${membership.role}`)}
      badges={{ requests: pending.data?.pending }}
      base={base}
      contextSeed={membership.clinicId}
      contextImageId={membership.clinicLogoId}
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
