import { NavLink, Outlet } from 'react-router'
import { useI18n } from '../../i18n/context'
import { PageHeader } from '../../ui/widgets'

export function AccountLayout() {
  const { t } = useI18n()
  return (
    <div className="page">
      <PageHeader title={t('account.title')} />
      <nav className="subnav" aria-label={t('account.title')}>
        <NavLink to="/me" end>
          {t('account.tabProfile')}
        </NavLink>
        <NavLink to="/me/security">{t('account.tabSecurity')}</NavLink>
        <NavLink to="/me/invitations">{t('account.tabInvitations')}</NavLink>
        <NavLink to="/me/appointments">{t('nav.myAppointments')}</NavLink>
      </nav>
      <Outlet />
    </div>
  )
}
