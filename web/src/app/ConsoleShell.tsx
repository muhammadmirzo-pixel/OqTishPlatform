import { useState, type ReactNode } from 'react'
import { Link, NavLink, useNavigate } from 'react-router'
import { useSession } from '../auth/session'
import { useI18n } from '../i18n/context'
import type { MenuItem } from '../features/clinic/permissions'
import { LanguageSwitcher } from '../ui/widgets'

/** Sidebar + topbar frame shared by the clinic console and the platform admin area. */
export function ConsoleShell({
  title,
  subtitle,
  base,
  items,
  linkSuffix = '',
  topbar,
  banner,
  children,
}: {
  title: string
  subtitle?: ReactNode
  base: string
  items: MenuItem[]
  linkSuffix?: string
  topbar?: ReactNode
  banner?: ReactNode
  children: ReactNode
}) {
  const { t } = useI18n()
  const { signOut } = useSession()
  const navigate = useNavigate()
  const [open, setOpen] = useState(false)

  return (
    <div className="console">
      <a className="skip-link" href="#main">
        {t('skip.toContent')}
      </a>
      <aside className={open ? 'console__sidebar console__sidebar--open' : 'console__sidebar'}>
        <div className="console__brand-row">
          <Link to="/" className="brand">
            <span className="brand__mark" aria-hidden="true">
              ◆
            </span>{' '}
            {t('app.name')}
          </Link>
          <button type="button" className="btn btn--ghost btn--small console__toggle" aria-expanded={open} aria-controls="console-nav" onClick={() => setOpen(!open)}>
            {t('nav.menu')}
          </button>
        </div>
        <p className="console__title">{title}</p>
        {subtitle}
        <nav id="console-nav" className="sidenav" aria-label={t('nav.menu')}>
          {items.map((item) => {
            const to = item.path ? `${base}/${item.path}` : base
            return (
              <NavLink key={item.path} to={`${to}${linkSuffix}`} end={item.path === ''} onClick={() => setOpen(false)}>
                {t(item.label)}
              </NavLink>
            )
          })}
        </nav>
      </aside>
      <div className="console__body">
        <header className="console__topbar">
          {topbar}
          <span className="spacer" />
          <LanguageSwitcher />
          <Link to="/me" className="btn btn--ghost btn--small">
            {t('nav.account')}
          </Link>
          <button
            type="button"
            className="btn btn--ghost btn--small"
            onClick={async () => {
              await signOut().catch(() => undefined)
              navigate('/login')
            }}
          >
            {t('common.signOut')}
          </button>
        </header>
        {banner}
        <main id="main" className="console__main" tabIndex={-1}>
          {children}
        </main>
      </div>
    </div>
  )
}
