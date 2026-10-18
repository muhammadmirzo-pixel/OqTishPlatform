import { useApi } from '../../api/context'
import { clinicVerifications } from '../../api/types'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { QueryView } from '../../ui/states'
import { PageHeader, StatCard } from '../../ui/widgets'

export function AdminDashboardPage() {
  const { t } = useI18n()
  useTitle(t('adminNav.dashboard'))
  const api = useApi()
  const dash = useQuery('admin-dashboard', () => api.admin.dashboard())
  return (
    <div className="stack">
      <PageHeader title={t('adminNav.dashboard')} />
      <QueryView query={dash}>
        {(d) => (
          <div className="stats">
            {clinicVerifications.map((s) => (
              <StatCard key={s} label={t('adm.clinicsBy', { status: t(`verification.${s}`) })} value={d.clinicsByStatus[s] ?? 0} tone={s === 'Pending' && (d.clinicsByStatus[s] ?? 0) > 0 ? 'warning' : undefined} />
            ))}
            <StatCard label={t('adm.appointmentsToday')} value={d.appointmentsToday} />
            <StatCard label={t('adm.openReports')} value={d.openReports} tone={d.openReports > 0 ? 'warning' : undefined} />
          </div>
        )}
      </QueryView>
    </div>
  )
}
