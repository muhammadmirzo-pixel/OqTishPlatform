import { ShieldCheck } from 'lucide-react'
import { Link, Outlet } from 'react-router'
import { useApi } from '../api/context'
import { useQuery } from '../hooks/useQuery'
import { useSession } from '../auth/session'
import { adminMenu, visibleMenu } from '../features/clinic/permissions'
import { useI18n } from '../i18n/context'
import { ConsoleShell } from './ConsoleShell'

export function AdminLayout() {
  const { me } = useSession()
  const { t } = useI18n()
  const api = useApi()
  const items = visibleMenu(adminMenu, me?.platformPermissions ?? [])
  // Work waiting for the platform: clinics to verify, open abuse reports (sidebar badges).
  const dash = useQuery(me && me.platformPermissions.length > 0 ? 'admin-nav-counts' : null, () => api.admin.dashboard(), { refreshMs: 60000 })
  return (
    <ConsoleShell
      title={t('console.adminTitle')}
      subtitle={
        <span className="badge badge--danger">
          <ShieldCheck className="icon" aria-hidden="true" />
          {t('console.platformBadge')}
        </span>
      }
      base="/admin"
      variant="admin"
      items={items}
      badges={{ clinics: dash.data?.clinicsByStatus.Pending, reports: dash.data?.openReports }}
      banner={
        me && (me.totpSetupRequired || !me.totpEnabled) ? (
          <div className="banner banner--warning" role="note">
            {t('console.totpBannerAdmin')} <Link to="/me/security">{t('state.totpLink')}</Link>
          </div>
        ) : undefined
      }
    >
      <Outlet />
    </ConsoleShell>
  )
}
