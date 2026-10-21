import { Building2, CalendarDays, ChevronDown, LogOut, ShieldCheck, ShieldHalf, UserRound } from 'lucide-react'
import { useEffect, useId, useRef, useState, type KeyboardEvent as ReactKeyboardEvent } from 'react'
import { Link, useNavigate } from 'react-router'
import { useSession } from '../auth/session'
import { useI18n } from '../i18n/context'
import { formatPhone } from '../lib/phone'
import { useAnchoredPopup } from '../ui/anchored'
import { Monogram } from '../ui/Monogram'
import { ConfirmDialog } from '../ui/ReasonDialog'

/**
 * Signed-in user's avatar button with a menu: account pages, own appointments, consoles and sign-out.
 * Sign-out asks for confirmation. `placement="up"` opens above the button (sidebar footer).
 */
export function UserMenu({ placement = 'down', variant = 'compact', subtitle }: { placement?: 'down' | 'up'; variant?: 'compact' | 'card'; subtitle?: string }) {
  const { t } = useI18n()
  const { me, signOut } = useSession()
  const navigate = useNavigate()
  const [open, setOpen] = useState(false)
  const [confirm, setConfirm] = useState(false)
  const root = useRef<HTMLDivElement>(null)
  const button = useRef<HTMLButtonElement>(null)
  const menuId = useId()
  useAnchoredPopup(open, [root], () => setOpen(false), () => undefined)

  useEffect(() => {
    if (!open) return
    root.current?.querySelector<HTMLElement>('[role="menuitem"]')?.focus()
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== 'Escape') return
      setOpen(false)
      button.current?.focus()
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [open])

  if (!me) return null
  const name = me.displayName || t('nav.account')
  const close = () => setOpen(false)

  function onMenuKey(e: ReactKeyboardEvent) {
    if (e.key !== 'ArrowDown' && e.key !== 'ArrowUp') return
    e.preventDefault()
    const items = [...(root.current?.querySelectorAll<HTMLElement>('[role="menuitem"]') ?? [])]
    const i = items.indexOf(document.activeElement as HTMLElement)
    items[(i + (e.key === 'ArrowDown' ? 1 : -1) + items.length) % items.length]?.focus()
  }

  return (
    <div ref={root} className={`user-menu user-menu--${variant} user-menu--${placement}`}>
      <button
        ref={button}
        type="button"
        className="user-menu__button"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={open ? menuId : undefined}
        aria-label={variant === 'compact' ? t('userMenu.label', { name }) : undefined}
        onClick={() => setOpen(!open)}
      >
        <Monogram name={name} seed={me.id} imageId={me.avatarId} size={variant === 'card' ? 36 : 30} round />
        <span className="user-menu__text">
          <strong>{name}</strong>
          {subtitle && <small>{subtitle}</small>}
        </span>
        <ChevronDown className="icon user-menu__chevron" aria-hidden="true" />
      </button>
      {open && (
        <div id={menuId} className="user-menu__popup" role="menu" aria-label={name} onKeyDown={onMenuKey}>
          <div className="user-menu__head">
            <Monogram name={name} seed={me.id} imageId={me.avatarId} size={40} round />
            <div>
              <strong>{name}</strong>
              {me.phoneMasked ? <small className="tabular">{formatPhone(me.phoneMasked)}</small> : me.email && <small>{me.email}</small>}
            </div>
          </div>
          <Link role="menuitem" to="/me" onClick={close}>
            <UserRound className="icon" aria-hidden="true" />
            {t('nav.account')}
          </Link>
          <Link role="menuitem" to="/me/security" onClick={close}>
            <ShieldHalf className="icon" aria-hidden="true" />
            {t('account.tabSecurity')}
          </Link>
          <Link role="menuitem" to="/me/appointments" onClick={close}>
            <CalendarDays className="icon" aria-hidden="true" />
            {t('nav.myAppointments')}
          </Link>
          {me.memberships.length > 0 && (
            <Link role="menuitem" to="/clinic" onClick={close}>
              <Building2 className="icon" aria-hidden="true" />
              {t('nav.clinicPanel')}
            </Link>
          )}
          {me.platformPermissions.length > 0 && (
            <Link role="menuitem" to="/admin" onClick={close}>
              <ShieldCheck className="icon" aria-hidden="true" />
              {t('nav.admin')}
            </Link>
          )}
          <hr />
          <button
            role="menuitem"
            type="button"
            className="user-menu__danger"
            onClick={() => {
              close()
              setConfirm(true)
            }}
          >
            <LogOut className="icon" aria-hidden="true" />
            {t('common.signOut')}
          </button>
        </div>
      )}
      {confirm && (
        <ConfirmDialog
          title={t('signOut.title')}
          body={t('signOut.body')}
          confirmLabel={t('common.signOut')}
          onConfirm={() => signOut().catch(() => undefined)}
          onDone={() => {
            setConfirm(false)
            navigate('/')
          }}
          onClose={() => setConfirm(false)}
        />
      )}
    </div>
  )
}
