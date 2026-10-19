import { CalendarDays, ChevronRight, LogOut, Mail, ShieldCheck, UserRound } from 'lucide-react'
import { NavLink, Outlet, useNavigate } from 'react-router'
import { useSession } from '../../auth/session'
import { useI18n } from '../../i18n/context'
import { initials } from '../../lib/initials'

/** Account area: profile card + section menu (sidebar on desktop, list on mobile) beside the page. */
export function AccountLayout() {
  const { t } = useI18n()
  const { me, signOut } = useSession()
  const navigate = useNavigate()
  const items = [
    { to: '/me', end: true, icon: UserRound, label: t('account.tabProfile') },
    { to: '/me/appointments', end: false, icon: CalendarDays, label: t('nav.myAppointments') },
    { to: '/me/security', end: false, icon: ShieldCheck, label: t('account.tabSecurity') },
    { to: '/me/invitations', end: false, icon: Mail, label: t('account.tabInvitations') },
  ]
  return (
    <div className="page account">
      <h1 className="visually-hidden">{t('account.title')}</h1>
      <div className="account__layout">
        <aside className="account__side">
          <div className="card account__who">
            <span className="avatar avatar--lg" aria-hidden="true">
              {initials(me?.displayName || '?')}
            </span>
            <div>
              <strong>{me?.displayName || t('account.title')}</strong>
              {me?.phoneMasked && <p className="muted small tabular">{me.phoneMasked}</p>}
            </div>
          </div>
          <nav className="card account__menu" aria-label={t('account.title')}>
            {items.map(({ to, end, icon: Icon, label }) => (
              <NavLink key={to} to={to} end={end}>
                <Icon className="icon" aria-hidden="true" />
                <span>{label}</span>
                <ChevronRight className="icon account__chev" aria-hidden="true" />
              </NavLink>
            ))}
            <button
              type="button"
              className="account__signout"
              onClick={async () => {
                await signOut().catch(() => undefined)
                navigate('/')
              }}
            >
              <LogOut className="icon" aria-hidden="true" />
              <span>{t('common.signOut')}</span>
            </button>
          </nav>
        </aside>
        <div className="account__main">
          <Outlet />
        </div>
      </div>
    </div>
  )
}
