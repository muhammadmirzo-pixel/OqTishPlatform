import { Link } from 'react-router'
import { useApi } from '../../api/context'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { formatMoney } from '../../lib/format'
import { QueryView } from '../../ui/states'
import { PageHeader, StatCard } from '../../ui/widgets'
import { useBranchParam } from './branchParam'
import { useClinic } from './clinicContext'
import { Perm } from './permissions'

export function DashboardPage() {
  const { t, locale } = useI18n()
  useTitle(t('clinicNav.dashboard'))
  const api = useApi()
  const { clinicId, membership, can, branchName } = useClinic()
  const [branch] = useBranchParam()
  const dash = useQuery(`dashboard:${clinicId}:${branch ?? ''}`, () => api.clinic.dashboard(clinicId, branch), { refreshMs: 60000 })
  const suffix = branch ? `?branch=${encodeURIComponent(branch)}` : ''
  const base = `/clinic/${clinicId}`

  return (
    <div className="stack">
      <PageHeader title={membership.clinicName} subtitle={branch ? branchName(branch) : t('dash.today')} />
      <QueryView query={dash}>
        {(d) => (
          <div className="stats">
            <StatCard label={t('dash.pending')} value={d.pending} tone={d.pending > 0 ? 'warning' : undefined} />
            <StatCard label={t('dash.confirmed')} value={d.confirmed} />
            <StatCard label={t('dash.completed')} value={d.completed} tone="success" />
            <StatCard label={t('dash.noShow')} value={d.noShow} />
            {d.collected && <StatCard label={t('dash.collected')} value={formatMoney(d.collected, locale)} />}
          </div>
        )}
      </QueryView>
      <section className="card stack" aria-labelledby="quick-title">
        <h2 id="quick-title">{t('dash.quick')}</h2>
        <div className="row">
          {can(Perm.triage) && (
            <Link className="btn" to={`${base}/requests${suffix}`}>
              {t('clinicNav.requests')}
            </Link>
          )}
          {can(Perm.walkIn) && (
            <Link className="btn btn--ghost" to={`${base}/walk-in${suffix}`}>
              {t('clinicNav.walkIn')}
            </Link>
          )}
          {can(Perm.appointmentView) && (
            <Link className="btn btn--ghost" to={`${base}/calendar${suffix}`}>
              {t('clinicNav.calendar')}
            </Link>
          )}
          {can(Perm.paymentRecord) && (
            <Link className="btn btn--ghost" to={`${base}/payments${suffix}`}>
              {t('clinicNav.payments')}
            </Link>
          )}
        </div>
      </section>
    </div>
  )
}
