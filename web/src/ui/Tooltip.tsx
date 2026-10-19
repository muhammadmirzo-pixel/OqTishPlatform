import { Info } from 'lucide-react'
import { useId, useState, type ReactNode } from 'react'
import { useI18n } from '../i18n/context'

/**
 * Small "(i)" button explaining a term. Opens on hover, focus or tap; the text is also exposed
 * through aria-describedby so screen readers get it without opening.
 */
export function InfoTip({ children, label }: { children: ReactNode; label?: string }) {
  const { t } = useI18n()
  const id = useId()
  const [open, setOpen] = useState(false)
  return (
    <span className="tooltip" onMouseEnter={() => setOpen(true)} onMouseLeave={() => setOpen(false)}>
      <button
        type="button"
        className="tooltip__trigger"
        aria-label={label ?? t('tip.more')}
        aria-describedby={id}
        aria-expanded={open}
        onClick={() => setOpen((v) => !v)}
        onFocus={() => setOpen(true)}
        onBlur={() => setOpen(false)}
        onKeyDown={(e) => e.key === 'Escape' && setOpen(false)}
      >
        <Info className="icon" aria-hidden="true" />
      </button>
      <span id={id} role="tooltip" className="tooltip__bubble" hidden={!open}>
        {children}
      </span>
    </span>
  )
}
