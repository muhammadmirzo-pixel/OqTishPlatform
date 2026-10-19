import { Check, ChevronDown } from 'lucide-react'
import { useCallback, useId, useLayoutEffect, useRef, useState, type CSSProperties, type KeyboardEvent, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { anchoredPosition, useAnchoredPopup, type AnchoredPosition } from './anchored'

export interface SelectOption {
  value: string
  label: string
  disabled?: boolean
}

export interface SelectProps {
  value: string
  onChange: (value: string) => void
  options: readonly SelectOption[]
  id?: string
  disabled?: boolean
  /** Shown when no option matches `value` (e.g. while the list loads). */
  placeholder?: string
  /** `field` = form control, `small` = toolbar/filter size, `bare` = text only (inside a chip or search bar). */
  variant?: 'field' | 'small' | 'bare'
  className?: string
  'aria-label'?: string
  'aria-labelledby'?: string
  'aria-describedby'?: string
  'aria-invalid'?: boolean | 'true'
}

const TYPEAHEAD_MS = 600

/**
 * Themed replacement for the native <select> (whose popup ignores the app theme and is unreadable
 * in dark mode). WAI-ARIA "select-only combobox": the trigger is a button with role="combobox";
 * focus stays on it while the listbox is open (aria-activedescendant). Keys: ↑ ↓ Home End PageUp
 * PageDown move, Enter/Space choose, Escape closes (without closing an enclosing dialog), Tab
 * chooses and moves on, letters jump to matching options. The listbox is portalled to <body> with
 * fixed coordinates so dialogs and scroll containers never clip it; it opens upwards when there is
 * no room below.
 */
export function Select({
  value,
  onChange,
  options,
  id,
  disabled,
  placeholder,
  variant = 'field',
  className = '',
  'aria-label': ariaLabel,
  'aria-labelledby': ariaLabelledby,
  'aria-describedby': ariaDescribedby,
  'aria-invalid': ariaInvalid,
}: SelectProps) {
  const auto = useId()
  const buttonId = id ?? `${auto}-trigger`
  const listId = `${auto}-list`
  const optionId = (i: number) => `${listId}-${i}`
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(-1)
  const [pos, setPos] = useState<AnchoredPosition | null>(null)
  const [listLabel, setListLabel] = useState<string | undefined>(undefined)
  const button = useRef<HTMLButtonElement>(null)
  const list = useRef<HTMLUListElement>(null)
  const typed = useRef({ text: '', at: 0 })

  const selectedIndex = options.findIndex((o) => o.value === value)
  const selected = selectedIndex >= 0 ? options[selectedIndex] : undefined
  const enabled = (i: number) => i >= 0 && i < options.length && !options[i]?.disabled

  const place = useCallback(() => {
    // A bare trigger sits inside a larger field or chip; the list lines up with that box instead.
    const anchor = variant === 'bare' ? (button.current?.parentElement ?? button.current) : button.current
    const r = anchor?.getBoundingClientRect()
    if (r) setPos(anchoredPosition(r, Math.min(320, options.length * 44 + 8)))
  }, [options.length, variant])

  function step(from: number, delta: number) {
    let i = from
    for (let n = 0; n < options.length; n++) {
      i = Math.min(options.length - 1, Math.max(0, i + delta))
      if (enabled(i)) return i
      if (i === 0 || i === options.length - 1) break
    }
    return from
  }
  const first = () => options.findIndex((o) => !o.disabled)
  const last = () => {
    for (let i = options.length - 1; i >= 0; i--) if (!options[i]?.disabled) return i
    return -1
  }

  function openList(at?: number) {
    if (disabled) return
    place()
    const labelled = ariaLabelledby ?? (document.getElementById(`${buttonId}-label`) ? `${buttonId}-label` : undefined)
    setListLabel(labelled)
    setActive(at ?? (enabled(selectedIndex) ? selectedIndex : first()))
    setOpen(true)
  }

  function close() {
    setOpen(false)
    setActive(-1)
  }

  function choose(i: number) {
    const o = options[i]
    if (!o || o.disabled) return
    if (o.value !== value) onChange(o.value)
    close()
    button.current?.focus()
  }

  function jumpTo(char: string) {
    const now = Date.now()
    const t = typed.current
    t.text = now - t.at > TYPEAHEAD_MS ? char : t.text + char
    t.at = now
    const query = t.text.toLocaleLowerCase()
    const repeated = query.length > 1 && [...query].every((c) => c === query[0])
    const needle = repeated ? query[0]! : query
    const start = (open ? active : selectedIndex) + (repeated || query.length === 1 ? 1 : 0)
    for (let n = 0; n < options.length; n++) {
      const i = (Math.max(start, 0) + n) % options.length
      if (enabled(i) && options[i]!.label.toLocaleLowerCase().startsWith(needle)) return i
    }
    return -1
  }

  function onKeyDown(e: KeyboardEvent<HTMLButtonElement>) {
    if (disabled) return
    const printable = e.key.length === 1 && !e.ctrlKey && !e.metaKey && !e.altKey && e.key !== ' '
    if (!open) {
      if (['ArrowDown', 'ArrowUp', 'Enter', ' '].includes(e.key)) {
        e.preventDefault()
        openList()
      } else if (e.key === 'Home' || e.key === 'End') {
        e.preventDefault()
        openList(e.key === 'Home' ? first() : last())
      } else if (printable) {
        const i = jumpTo(e.key)
        if (i >= 0) openList(i)
      }
      return
    }
    switch (e.key) {
      case 'ArrowDown':
        e.preventDefault()
        if (e.altKey) return
        setActive((a) => step(a < 0 ? -1 : a, 1))
        return
      case 'ArrowUp':
        e.preventDefault()
        if (e.altKey) choose(active)
        else setActive((a) => step(a < 0 ? options.length : a, -1))
        return
      case 'Home':
        e.preventDefault()
        setActive(first())
        return
      case 'End':
        e.preventDefault()
        setActive(last())
        return
      case 'PageDown':
        e.preventDefault()
        setActive((a) => step(Math.min(options.length - 1, a + 9), 1))
        return
      case 'PageUp':
        e.preventDefault()
        setActive((a) => step(Math.max(0, a - 9), -1))
        return
      case 'Enter':
      case ' ':
        e.preventDefault()
        choose(active)
        return
      case 'Escape':
        // Only the list closes; an enclosing dialog listens on document and must not see this key.
        e.preventDefault()
        e.stopPropagation()
        close()
        return
      case 'Tab':
        if (enabled(active)) {
          const o = options[active]!
          if (o.value !== value) onChange(o.value)
        }
        close()
        return
      default:
        if (printable) {
          const i = jumpTo(e.key)
          if (i >= 0) setActive(i)
        }
    }
  }

  // Close on outside press; keep the list attached to the trigger while the page scrolls or resizes.
  useAnchoredPopup(open, [button, list], close, place)

  useLayoutEffect(() => {
    if (!open || active < 0) return
    document.getElementById(optionId(active))?.scrollIntoView?.({ block: 'nearest' })
    // optionId depends only on listId, which is stable.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, active])

  const style: CSSProperties | undefined = pos
    ? { left: pos.left, minWidth: pos.minWidth, maxWidth: pos.maxWidth, top: pos.top, bottom: pos.bottom, maxHeight: pos.maxHeight }
    : undefined

  return (
    <>
      <button
        ref={button}
        id={buttonId}
        type="button"
        role="combobox"
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-controls={listId}
        aria-activedescendant={open && active >= 0 ? optionId(active) : undefined}
        aria-label={ariaLabel}
        aria-labelledby={ariaLabelledby}
        aria-describedby={ariaDescribedby}
        aria-invalid={ariaInvalid || undefined}
        data-value={value}
        disabled={disabled}
        className={`select select--${variant}${open ? ' select--open' : ''}${className ? ` ${className}` : ''}`}
        onClick={() => (open ? close() : openList())}
        onKeyDown={onKeyDown}
      >
        <span className={selected ? 'select__value' : 'select__value select__value--placeholder'}>{selected?.label ?? placeholder ?? ' '}</span>
        <ChevronDown className="icon select__chev" aria-hidden="true" />
      </button>
      {open &&
        createPortal(
          <ul
            ref={list}
            id={listId}
            role="listbox"
            className={pos?.bottom !== undefined ? 'select-pop select-pop--up' : 'select-pop'}
            style={style}
            aria-labelledby={listLabel}
            aria-label={listLabel ? undefined : ariaLabel}
            tabIndex={-1}
            // Keep focus on the trigger while choosing with the pointer.
            onMouseDown={(e) => e.preventDefault()}
          >
            {options.map((o, i) => (
              <li
                key={`${o.value}-${i}`}
                id={optionId(i)}
                role="option"
                aria-selected={o.value === value}
                aria-disabled={o.disabled || undefined}
                data-value={o.value}
                className={`select-pop__option${i === active ? ' is-active' : ''}`}
                onMouseMove={() => !o.disabled && i !== active && setActive(i)}
                onClick={() => choose(i)}
              >
                <span>{o.label}</span>
                {o.value === value && <Check className="icon" aria-hidden="true" />}
              </li>
            ))}
          </ul>,
          document.body,
        )}
    </>
  )
}

/** Label + Select for filters and toolbars (the label is a real <label for>, so clicking it opens the list). */
export function SelectField({ label, inline = false, className = '', ...props }: SelectProps & { label: ReactNode; inline?: boolean }) {
  const auto = useId()
  const id = props.id ?? `${auto}-select`
  return (
    <div className={`field${inline ? ' field--inline' : ''}${className ? ` ${className}` : ''}`}>
      <label id={`${id}-label`} htmlFor={id}>
        {label}
      </label>
      <Select {...props} id={id} variant={props.variant ?? (inline ? 'small' : 'field')} />
    </div>
  )
}
