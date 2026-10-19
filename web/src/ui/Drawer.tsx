import { X } from 'lucide-react'
import { useId, useRef, type ReactNode } from 'react'
import { useI18n } from '../i18n/context'
import { useModal } from './useModal'

/** Side or bottom sheet (mobile filters, navigation). Same focus rules as Dialog. */
export function Drawer({
  title,
  onClose,
  side = 'bottom',
  footer,
  children,
}: {
  title: string
  onClose: () => void
  side?: 'bottom' | 'right' | 'left'
  footer?: ReactNode
  children: ReactNode
}) {
  const { t } = useI18n()
  const titleId = useId()
  const panel = useRef<HTMLDivElement>(null)
  useModal(panel, onClose, 'input, select, textarea, button:not(.drawer__close)')
  return (
    <>
      <div className="drawer-backdrop" onMouseDown={onClose} />
      <div className={`drawer drawer--${side}`} role="dialog" aria-modal="true" aria-labelledby={titleId} ref={panel} tabIndex={-1}>
        <header className="drawer__header">
          <h2 id={titleId}>{title}</h2>
          <button type="button" className="btn btn--ghost btn--small btn--icon drawer__close" onClick={onClose} aria-label={t('drawer.close')}>
            <X className="icon" aria-hidden="true" />
          </button>
        </header>
        <div className="drawer__body">{children}</div>
        {footer && <div className="drawer__footer">{footer}</div>}
      </div>
    </>
  )
}
