import { useState } from 'react'
import { Link, NavLink, Outlet } from 'react-router'
import type { StaffRole } from '../domain/types'
import { useI18n } from '../i18n/context'
import type { MessageKey } from '../i18n/messages'
import { canSee, type ClinicSection } from '../features/clinic/permissions'
import { DemoRoleContext } from '../features/clinic/roleContext'
import { LanguageSwitcher } from '../ui/widgets'

interface NavItem {
  to: string
  label: MessageKey
  section?: ClinicSection
}

const clinicNav: NavItem[] = [
  { to: '/clinic', label: 'clinicNav.dashboard', section: 'dashboard' },
  { to: '/clinic/requests', label: 'clinicNav.requests', section: 'requests' },
  { to: '/clinic/calendar', label: 'clinicNav.calendar', section: 'calendar' },
  { to: '/clinic/doctors', label: 'clinicNav.doctors', section: 'doctors' },
  { to: '/clinic/services', label: 'clinicNav.services', section: 'services' },
  { to: '/clinic/staff', label: 'clinicNav.staff', section: 'staff' },
  { to: '/clinic/payments', label: 'clinicNav.payments', section: 'payments' },
  { to: '/clinic/reports', label: 'clinicNav.reports', section: 'reports' },
]

const adminNav: NavItem[] = [
  { to: '/admin', label: 'adminNav.dashboard' },
  { to: '/admin/clinics', label: 'adminNav.clinics' },
  { to: '/admin/accounts', label: 'adminNav.accounts' },
  { to: '/admin/audit', label: 'adminNav.audit' },
]

const roles: StaffRole[] = ['ClinicOwner', 'ClinicAdmin', 'Receptionist', 'Doctor', 'FinanceManager']

export function ConsoleLayout({ kind }: { kind: 'clinic' | 'admin' }) {
  const { t } = useI18n()
  const [role, setRole] = useState<StaffRole>('ClinicOwner')
  const items = kind === 'clinic' ? clinicNav.filter((i) => !i.section || canSee(role, i.section)) : adminNav

  return (
    <DemoRoleContext.Provider value={{ role, setRole }}>
      <div className={`console console--${kind}`}>
        <aside className="console__sidebar">
          <Link to="/" className="brand">
            <span className="brand__mark" aria-hidden="true">🦷</span> {t('app.name')}
          </Link>
          <p className="console__title">{t(kind === 'clinic' ? 'console.clinicTitle' : 'console.adminTitle')}</p>
          <nav className="sidenav" aria-label={t('nav.menu')}>
            {items.map((item) => (
              <NavLink key={item.to} to={item.to} end={item.to === '/clinic' || item.to === '/admin'}>
                {t(item.label)}
              </NavLink>
            ))}
          </nav>
        </aside>
        <div className="console__body">
          <header className="console__topbar">
            {kind === 'clinic' && (
              <label className="field field--inline" title={t('console.roleNote')}>
                <span>{t('console.role')}</span>
                <select value={role} onChange={(e) => setRole(e.target.value as StaffRole)}>
                  {roles.map((r) => (
                    <option key={r} value={r}>
                      {t(`role.${r}`)}
                    </option>
                  ))}
                </select>
              </label>
            )}
            <span className="spacer" />
            <LanguageSwitcher />
            <Link to="/" className="btn btn--ghost btn--small">
              {t('console.toSite')}
            </Link>
          </header>
          <main className="console__main">
            <Outlet />
          </main>
        </div>
      </div>
    </DemoRoleContext.Provider>
  )
}
