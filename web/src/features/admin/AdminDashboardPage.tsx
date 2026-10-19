import { Ban, BadgeCheck, CalendarCheck, Hourglass, LayoutDashboard, MessageSquareWarning, UserX, Users, XCircle, type LucideIcon } from 'lucide-react'
import { useApi } from '../../api/context'
import { clinicVerifications, type ClinicVerification } from '../../api/types'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { QueryView } from '../../ui/states'
import { PageHeader, StatCard } from '../../ui/widgets'

const verificationIcon: Record<ClinicVerification, [LucideIcon, 'warning' | 'teal' | 'neutral' | 'danger']> = {
  Pending: [Hourglass, 'warning'],
  Approved: [BadgeCheck, 'teal'],
  Rejected: [XCircle, 'neutral'],
  Suspended: [Ban, 'danger'],
}

export function AdminDashboardPage() {
  const { t } = useI18n()
  useTitle(t('adminNav.dashboard'))
  const api = useApi()
  const dash = useQuery('admin-dashboard', () => api.admin.dashboard())
  return (
    <div className="stack">
      <PageHeader icon={LayoutDashboard} title={t('adminNav.dashboard')} />
      <QueryView query={dash}>
        {(d) => (
          <div className="stats">
            {clinicVerifications.map((s) => (
              <StatCard
                key={s}
                label={t('adm.clinicsBy', { status: t(`verification.${s}`) })}
                value={d.clinicsByStatus[s] ?? 0}
                tone={s === 'Pending' && (d.clinicsByStatus[s] ?? 0) > 0 ? 'warning' : undefined}
                icon={verificationIcon[s][0]}
                iconTone={verificationIcon[s][1]}
              />
            ))}
            <StatCard label={t('adm.appointmentsToday')} value={d.appointmentsToday} icon={CalendarCheck} />
            <StatCard label={t('adm.openReports')} value={d.openReports} tone={d.openReports > 0 ? 'warning' : undefined} icon={MessageSquareWarning} iconTone="warning" />
            <StatCard label={t('adm.activeUsers')} value={d.activeUsers} icon={Users} iconTone="teal" />
            <StatCard label={t('adm.suspendedUsers')} value={d.suspendedUsers} tone={d.suspendedUsers > 0 ? 'warning' : undefined} icon={UserX} iconTone="danger" />
          </div>
        )}
      </QueryView>
    </div>
  )
}
