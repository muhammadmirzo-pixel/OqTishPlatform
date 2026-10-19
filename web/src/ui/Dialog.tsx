import { X } from 'lucide-react'
import { useId, useRef, type ReactNode } from 'react'
import { useI18n } from '../i18n/context'
import { useModal } from './useModal'

/**
 * Modal dialog: focus moves into it on open and back to the opener on close; Tab is trapped,
 * Escape and the close button dismiss it. `tone="danger"` marks destructive confirmations.
 */
export function Dialog({ title, onClose, children, tone }: { title: string; onClose: () => void; children: ReactNode; tone?: 'danger' }) {
  const { t } = useI18n()
  const titleId = useId()
  const panel = useRef<HTMLDivElement>(null)
  useModal(panel, onClose, 'input, select, textarea, button:not(.dialog__close)')

  return (
    <div className="dialog-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <div className={tone === 'danger' ? 'dialog dialog--danger' : 'dialog'} role="dialog" aria-modal="true" aria-labelledby={titleId} ref={panel} tabIndex={-1}>
        <header className="dialog__header">
          <h2 id={titleId}>{title}</h2>
          <button type="button" className="btn btn--ghost btn--small btn--icon dialog__close" onClick={onClose} aria-label={t('common.close')}>
            <X className="icon" aria-hidden="true" />
          </button>
        </header>
        <div className="dialog__body">{children}</div>
      </div>
    </div>
  )
}
