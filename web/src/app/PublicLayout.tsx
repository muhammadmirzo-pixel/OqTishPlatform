import { useState } from 'react'
import { Link, NavLink, Outlet } from 'react-router'
import { useI18n } from '../i18n/context'
import { LanguageSwitcher } from '../ui/widgets'
import { ServerStatus } from './ServerStatus'

export function PublicLayout() {
  const { t } = useI18n()
  const [open, setOpen] = useState(false)
  const close = () => setOpen(false)

  return (
    <div className="site">
      <header className="topbar">
        <Link to="/" className="brand" onClick={close}>
          <span className="brand__mark" aria-hidden="true">🦷</span> {t('app.name')}
        </Link>
        <button type="button" className="topbar__toggle btn btn--ghost" aria-expanded={open} aria-controls="site-nav" onClick={() => setOpen(!open)}>
          {t('nav.menu')}
        </button>
        <nav id="site-nav" className={open ? 'topnav topnav--open' : 'topnav'} aria-label={t('nav.menu')}>
          <NavLink to="/" end onClick={close}>{t('nav.clinics')}</NavLink>
          <NavLink to="/me/appointments" onClick={close}>{t('nav.myAppointments')}</NavLink>
          <NavLink to="/clinic" onClick={close}>{t('nav.forClinics')}</NavLink>
          <NavLink to="/admin" onClick={close}>{t('nav.admin')}</NavLink>
          <NavLink to="/login" className="btn btn--small" onClick={close}>{t('nav.login')}</NavLink>
          <LanguageSwitcher />
        </nav>
      </header>
      <main className="site__main">
        <Outlet />
      </main>
      <footer className="footer">
        <span>{t('app.tagline')}</span>
        <ServerStatus />
      </footer>
    </div>
  )
}
