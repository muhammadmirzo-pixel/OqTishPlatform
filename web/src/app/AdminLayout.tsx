import { Link, Outlet } from 'react-router'
import { useSession } from '../auth/session'
import { adminMenu, visibleMenu } from '../features/clinic/permissions'
import { useI18n } from '../i18n/context'
import { ConsoleShell } from './ConsoleShell'

export function AdminLayout() {
  const { me } = useSession()
  const { t } = useI18n()
  const items = visibleMenu(adminMenu, me?.platformPermissions ?? [])
  return (
    <ConsoleShell
      title={t('console.adminTitle')}
      base="/admin"
      variant="admin"
      items={items}
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
