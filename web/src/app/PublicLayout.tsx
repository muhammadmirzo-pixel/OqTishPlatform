import { useState } from 'react'
import { Link, NavLink, Outlet, useNavigate } from 'react-router'
import { useSession } from '../auth/session'
import { useI18n } from '../i18n/context'
import { LanguageSwitcher } from '../ui/widgets'
import { ServerStatus } from './ServerStatus'

export function PublicLayout() {
  const { t } = useI18n()
  const { me, status, signOut } = useSession()
  const navigate = useNavigate()
  const [open, setOpen] = useState(false)
  const close = () => setOpen(false)

  return (
    <div className="site">
      <a className="skip-link" href="#main">
        {t('skip.toContent')}
      </a>
      <header className="topbar">
        <Link to="/" className="brand" onClick={close}>
          <span className="brand__mark" aria-hidden="true">
            ◆
          </span>{' '}
          {t('app.name')}
        </Link>
        <button type="button" className="topbar__toggle btn btn--ghost" aria-expanded={open} aria-controls="site-nav" onClick={() => setOpen(!open)}>
          {t('nav.menu')}
        </button>
        <nav id="site-nav" className={open ? 'topnav topnav--open' : 'topnav'} aria-label={t('nav.menu')}>
          <NavLink to="/" end onClick={close}>
            {t('nav.clinics')}
          </NavLink>
          {me && (
            <NavLink to="/me/appointments" onClick={close}>
              {t('nav.myAppointments')}
            </NavLink>
          )}
          <NavLink to={me && me.memberships.length > 0 ? '/clinic' : '/clinic/register'} onClick={close}>
            {t('nav.forClinics')}
          </NavLink>
          {me && me.platformPermissions.length > 0 && (
            <NavLink to="/admin" onClick={close}>
              {t('nav.admin')}
            </NavLink>
          )}
          {status === 'authenticated' && me ? (
            <>
              <NavLink to="/me" end onClick={close}>
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
          <LanguageSwitcher />
        </nav>
      </header>
      <main id="main" className="site__main" tabIndex={-1}>
        <Outlet />
      </main>
      <footer className="footer">
        <span>{t('app.tagline')}</span>
        <ServerStatus />
      </footer>
    </div>
  )
}
