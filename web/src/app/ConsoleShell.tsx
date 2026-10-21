import { Menu, X } from 'lucide-react'
import { useState, type ReactNode } from 'react'
import { Link, NavLink } from 'react-router'
import { useI18n } from '../i18n/context'
import type { MenuItem } from '../features/clinic/permissions'
import { Logo } from '../ui/Logo'
import { Monogram } from '../ui/Monogram'
import { ThemeSwitcher } from '../ui/ThemeSwitcher'
import { LanguageSwitcher } from '../ui/widgets'
import { menuIcon, menuSection } from './menuMeta'
import { UserMenu } from './UserMenu'

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
  contextImageId,
  badges,
  userRole,
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
  /** Clinic logo shown in the context card. */
  contextImageId?: string | null
  /** Count shown next to a menu item, keyed by its path (e.g. waiting requests). */
  badges?: Partial<Record<string, number>>
  /** Shown under the user's name at the bottom of the sidebar. */
  userRole?: string
  children: ReactNode
}) {
  const { t } = useI18n()
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
          <button type="button" className="btn btn--ghost btn--small console__toggle" aria-expanded={open} aria-controls="console-nav" onClick={() => setOpen(!open)}>
            {open ? <X className="icon" aria-hidden="true" /> : <Menu className="icon" aria-hidden="true" />}
            {t('nav.menu')}
          </button>
        </div>
        <div className="console__context">
          <Monogram name={title} seed={contextSeed} imageId={contextImageId} size={40} />
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
            const count = badges?.[item.path]
            return (
              <div key={item.path} className="sidenav__item">
                {heading && <p className="sidenav__label">{t(heading)}</p>}
                <NavLink to={`${to}${linkSuffix}`} end={item.path === ''} onClick={() => setOpen(false)}>
                  <span className="sidenav__icon">{Icon && <Icon className="icon" aria-hidden="true" />}</span>
                  {t(item.label)}
                  {count ? <span className="sidenav__count">{count}</span> : null}
                </NavLink>
              </div>
            )
          })}
        </nav>
        <div className="console__foot">
          <div className="console__prefs console__prefs--menu">
            <LanguageSwitcher />
            <ThemeSwitcher />
          </div>
          <UserMenu placement="up" variant="card" subtitle={userRole} />
        </div>
      </aside>
      <div className="console__body">
        <header className="console__topbar">
          {topbar}
          <span className="spacer" />
          <div className="console__prefs console__prefs--top">
            <LanguageSwitcher />
            <ThemeSwitcher />
          </div>
          <div className="console__topbar-user">
            <UserMenu />
          </div>
        </header>
        {banner}
        <main id="main" className="console__main" tabIndex={-1}>
          {children}
        </main>
      </div>
    </div>
  )
}
