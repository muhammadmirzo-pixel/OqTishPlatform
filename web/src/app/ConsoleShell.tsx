import { LogOut, Menu, UserRound } from 'lucide-react'
import { useState, type ReactNode } from 'react'
import { Link, NavLink, useNavigate } from 'react-router'
import { useSession } from '../auth/session'
import { useI18n } from '../i18n/context'
import type { MenuItem } from '../features/clinic/permissions'
import { Logo } from '../ui/Logo'
import { Monogram } from '../ui/Monogram'
import { ThemeSwitcher } from '../ui/ThemeSwitcher'
import { LanguageSwitcher } from '../ui/widgets'
import { menuIcon, menuSection } from './menuMeta'

/** Sidebar + topbar frame shared by the clinic console and the platform admin area. */
export function ConsoleShell({
  title,
  subtitle,
  base,
  items,
  linkSuffix = '',
  topbar,
  banner,
  variant = 'clinic',
  contextSeed,
  children,
}: {
  title: string
  subtitle?: ReactNode
  base: string
  items: MenuItem[]
  linkSuffix?: string
  topbar?: ReactNode
  banner?: ReactNode
  variant?: 'clinic' | 'admin'
  contextSeed?: string
  children: ReactNode
}) {
  const { t } = useI18n()
  const { me, signOut } = useSession()
  const navigate = useNavigate()
  const [open, setOpen] = useState(false)

  const headings = items.map((item, i) => {
    const section = menuSection[item.label]
    const prev = items.slice(0, i).reverse().find((x) => menuSection[x.label])
    return section && section !== (prev && menuSection[prev.label]) ? section : undefined
  })
  return (
    <div className={variant === 'admin' ? 'console admin' : 'console'}>
      <a className="skip-link" href="#main">
        {t('skip.toContent')}
      </a>
      <aside className={open ? 'console__sidebar console__sidebar--open' : 'console__sidebar'}>
        <div className="console__brand-row">
          <Link to="/" className="brand" aria-label={t('app.name')}>
            <Logo tagline={false} />
          </Link>
          <button type="button" className="btn btn--small console__toggle" aria-expanded={open} aria-controls="console-nav" onClick={() => setOpen(!open)}>
            <Menu className="icon" aria-hidden="true" />
            {t('nav.menu')}
          </button>
        </div>
        <div className="console__context">
          <Monogram name={title} seed={contextSeed} size={36} />
          <div>
            <p className="console__title">{title}</p>
            {subtitle}
          </div>
        </div>
        <nav id="console-nav" className="sidenav" aria-label={t('nav.menu')}>
          {items.map((item, i) => {
            const to = item.path ? `${base}/${item.path}` : base
            const Icon = menuIcon[item.label]
            const heading = headings[i]
            return (
              <div key={item.path} className="sidenav__item">
                {heading && <p className="sidenav__label">{t(heading)}</p>}
                <NavLink to={`${to}${linkSuffix}`} end={item.path === ''} onClick={() => setOpen(false)}>
                  {Icon && <Icon className="icon" aria-hidden="true" />}
                  {t(item.label)}
                </NavLink>
              </div>
            )
          })}
        </nav>
      </aside>
      <div className="console__body">
        <header className="console__topbar">
          {topbar}
          <span className="spacer" />
          <LanguageSwitcher />
          <ThemeSwitcher />
          <Link to="/me" className="btn btn--ghost btn--small">
            <UserRound className="icon" aria-hidden="true" />
            <span className="console__user">{me?.displayName || t('nav.account')}</span>
          </Link>
          <button
            type="button"
            className="btn btn--ghost btn--small btn--icon"
            aria-label={t('common.signOut')}
            title={t('common.signOut')}
            onClick={async () => {
              await signOut().catch(() => undefined)
              navigate('/login')
            }}
          >
            <LogOut className="icon" aria-hidden="true" />
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
