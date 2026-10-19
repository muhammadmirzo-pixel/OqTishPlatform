import { CalendarDays, Home, LogOut, Menu, UserRound, X } from 'lucide-react'
import { useState } from 'react'
import { Link, NavLink, Outlet, useNavigate } from 'react-router'
import { useSession } from '../auth/session'
import { useI18n } from '../i18n/context'
import { Logo } from '../ui/Logo'
import { ThemeSwitcher } from '../ui/ThemeSwitcher'
import { LanguageSwitcher } from '../ui/widgets'
import { ServerStatus } from './ServerStatus'

export function PublicLayout() {
  const { t } = useI18n()
  const { me, status, signOut } = useSession()
  const navigate = useNavigate()
  const [open, setOpen] = useState(false)
  const [year] = useState(() => new Date().getFullYear())
  const close = () => setOpen(false)
  const clinicLink = me && me.memberships.length > 0 ? '/clinic' : '/clinic/register'

  return (
    <div className="site has-tabbar">
      <a className="skip-link" href="#main">
        {t('skip.toContent')}
      </a>
      <header className="topbar">
        <div className="topbar__inner">
          <Link to="/" className="brand" onClick={close} aria-label={t('app.name')}>
            <Logo />
          </Link>
          <button type="button" className="topbar__toggle btn btn--ghost btn--small" aria-expanded={open} aria-controls="site-nav" onClick={() => setOpen(!open)}>
            {open ? <X className="icon" aria-hidden="true" /> : <Menu className="icon" aria-hidden="true" />}
            {t('nav.menu')}
          </button>
          <nav id="site-nav" className={open ? 'topnav topnav--open' : 'topnav'} aria-label={t('nav.menu')}>
            <div className="topnav__links">
              <NavLink to="/" end onClick={close}>
                {t('nav.home')}
              </NavLink>
              <NavLink to="/clinics" onClick={close}>
                {t('nav.clinics')}
              </NavLink>
              {me && (
                <NavLink to="/me/appointments" onClick={close}>
                  {t('nav.myAppointments')}
                </NavLink>
              )}
              <NavLink to={clinicLink} onClick={close}>
                {t('nav.forClinics')}
              </NavLink>
              {me && me.platformPermissions.length > 0 && (
                <NavLink to="/admin" onClick={close}>
                  {t('nav.admin')}
                </NavLink>
              )}
            </div>
            <div className="topnav__actions">
              <LanguageSwitcher />
              <ThemeSwitcher />
              {status === 'authenticated' && me ? (
                <>
                  <NavLink to="/me" end className="btn btn--ghost btn--small" onClick={close}>
                    <UserRound className="icon" aria-hidden="true" />
                    {t('nav.account')}
                  </NavLink>
                  <button
                    type="button"
                    className="btn btn--ghost btn--small"
                    onClick={async () => {
                      close()
                      await signOut().catch(() => undefined)
                      navigate('/')
                    }}
                  >
                    <LogOut className="icon" aria-hidden="true" />
                    {t('common.signOut')}
                  </button>
                </>
              ) : (
                status === 'anonymous' && (
                  <NavLink to="/login" className="btn btn--small" onClick={close}>
                    {t('nav.login')}
                  </NavLink>
                )
              )}
            </div>
          </nav>
        </div>
      </header>
      <main id="main" className="site__main" tabIndex={-1}>
        <Outlet />
      </main>
      <footer className="footer">
        <div className="footer__inner">
          <div className="stack stack--sm">
            <Link to="/" className="brand" aria-label={t('app.name')}>
              <Logo />
            </Link>
            <p>{t('footer.about')}</p>
            <p className="small">{t('footer.paymentNote')}</p>
          </div>
          <div>
            <h2>{t('footer.patients')}</h2>
            <ul>
              <li>
                <Link to="/clinics">{t('footer.findClinic')}</Link>
              </li>
              <li>
                <Link to="/me/appointments">{t('footer.myAppointments')}</Link>
              </li>
            </ul>
          </div>
          <div>
            <h2>{t('footer.clinics')}</h2>
            <ul>
              <li>
                <Link to="/clinic/register">{t('footer.registerClinic')}</Link>
              </li>
              <li>
                <Link to="/clinic">{t('footer.clinicPanel')}</Link>
              </li>
            </ul>
          </div>
          <div>
            <h2>{t('footer.platform')}</h2>
            <ul>
              <li>{t('app.tagline')}</li>
            </ul>
          </div>
        </div>
        <div className="footer__bottom">
          <span>{t('footer.rights', { year })}</span>
          <ServerStatus />
        </div>
      </footer>
      <nav className="tabbar" aria-label={t('tabbar.label')}>
        <NavLink to="/" end>
          <Home className="icon" aria-hidden="true" />
          {t('nav.home')}
        </NavLink>
        <NavLink to="/me/appointments">
          <CalendarDays className="icon" aria-hidden="true" />
          {t('nav.myAppointments')}
        </NavLink>
        <NavLink to={me ? '/me' : '/login'} end>
          <UserRound className="icon" aria-hidden="true" />
          {t('nav.profile')}
        </NavLink>
      </nav>
    </div>
  )
}
