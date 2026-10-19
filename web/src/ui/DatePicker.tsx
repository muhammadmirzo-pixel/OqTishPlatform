import { CalendarDays, ChevronLeft, ChevronRight, Clock } from 'lucide-react'
import { useId, useLayoutEffect, useMemo, useRef, useState, type CSSProperties, type KeyboardEvent, type ReactNode, type RefObject } from 'react'
import { createPortal } from 'react-dom'
import { useI18n } from '../i18n/context'
import { formatDate, monthName, weekdayNamesShort } from '../lib/format'
import { addDays, DEFAULT_TIME_ZONE, fromMinutes, isValidDate, localDate, toMinutes, weekdayOf } from '../lib/time'
import { anchoredPosition, useAnchoredPopup, type AnchoredPosition } from './anchored'
import { Select, type SelectProps } from './Select'

interface AriaProps {
  id?: string
  disabled?: boolean
  'aria-label'?: string
  'aria-labelledby'?: string
  'aria-describedby'?: string
  'aria-invalid'?: boolean | 'true'
}

const capitalize = (s: string) => s.charAt(0).toLocaleUpperCase() + s.slice(1)
const pad = (n: number) => String(n).padStart(2, '0')

/** Same day `n` months later, clamped to the month's length ("2026-01-31" + 1 → "2026-02-28"). */
function addMonths(date: string, n: number): string {
  const [y = 1970, m = 1, d = 1] = date.split('-').map(Number)
  const total = y * 12 + (m - 1) + n
  const ny = Math.floor(total / 12)
  const nm = (total % 12) + 1
  const last = new Date(Date.UTC(ny, nm, 0)).getUTCDate()
  return `${ny}-${pad(nm)}-${pad(Math.min(d, last))}`
}

const FOCUSABLE = 'button:not(:disabled), [tabindex="0"]'

/** Popover position next to the trigger, computed at event time (open, scroll, resize). */
function usePlacement(trigger: RefObject<HTMLElement | null>) {
  const [pos, setPos] = useState<AnchoredPosition | null>(null)
  const place = () => {
    const r = trigger.current?.getBoundingClientRect()
    if (r) setPos(anchoredPosition(r, 400, { minWidth: 0, maxWidth: 360, maxHeight: 440, fitWhole: true }))
  }
  return [pos, place] as const
}

/**
 * Shared popover for the pickers: portalled to <body> with fixed coordinates, a non-modal dialog.
 * Escape closes it (and only it — an enclosing modal dialog listens on document); Tab cycles inside
 * it, because the enclosing dialog's focus trap cannot see a portalled popover.
 */
function PickerPopover({
  anchor,
  pos,
  onMove,
  label,
  onClose,
  className,
  children,
}: {
  anchor: RefObject<HTMLElement | null>
  pos: AnchoredPosition | null
  onMove: () => void
  label: string
  onClose: (restoreFocus: boolean) => void
  className: string
  children: ReactNode
}) {
  const panel = useRef<HTMLDivElement>(null)
  useAnchoredPopup(true, [anchor, panel], () => onClose(false), onMove)

  function onKeyDown(e: KeyboardEvent<HTMLDivElement>) {
    if (e.key === 'Escape') {
      e.preventDefault()
      e.stopPropagation()
      onClose(true)
      return
    }
    if (e.key !== 'Tab' || !panel.current) return
    const items = [...panel.current.querySelectorAll<HTMLElement>(FOCUSABLE)]
    if (items.length === 0) return
    e.preventDefault()
    e.stopPropagation()
    const i = items.indexOf(document.activeElement as HTMLElement)
    const next = e.shiftKey ? (i <= 0 ? items.length - 1 : i - 1) : i === items.length - 1 ? 0 : i + 1
    items[next]?.focus()
  }

  const style: CSSProperties | undefined = pos ? { left: pos.left, top: pos.top, bottom: pos.bottom, maxHeight: pos.maxHeight } : { visibility: 'hidden' }
  return createPortal(
    <div ref={panel} role="dialog" aria-modal="false" aria-label={label} className={`picker-pop ${className}`} style={style} onKeyDown={onKeyDown}>
      {children}
    </div>,
    document.body,
  )
}

function Trigger({
  open,
  icon,
  text,
  placeholder,
  variant,
  triggerRef,
  onClick,
  aria,
}: {
  open: boolean
  icon: ReactNode
  text: string | undefined
  placeholder: string
  variant: 'field' | 'small'
  triggerRef: RefObject<HTMLButtonElement | null>
  onClick: () => void
  aria: AriaProps
}) {
  return (
    <button
      ref={triggerRef}
      id={aria.id}
      type="button"
      aria-haspopup="dialog"
      aria-expanded={open}
      aria-label={aria['aria-label']}
      aria-labelledby={aria['aria-labelledby']}
      aria-describedby={aria['aria-describedby']}
      aria-invalid={aria['aria-invalid'] || undefined}
      disabled={aria.disabled}
      className={`select select--${variant} picker-trigger${open ? ' select--open' : ''}`}
      onClick={onClick}
    >
      {icon}
      <span className={text ? 'select__value' : 'select__value select__value--placeholder'}>{text ?? placeholder}</span>
    </button>
  )
}

export interface DatePickerProps extends AriaProps {
  /** "YYYY-MM-DD" or "". */
  value: string
  onChange: (value: string) => void
  min?: string
  max?: string
  /** Branch-local today (defaults to Asia/Tashkent). */
  today?: string
  placeholder?: string
  clearable?: boolean
  variant?: 'field' | 'small'
}

/**
 * Themed date picker (replaces <input type="date">, whose format and calendar follow the browser,
 * not the app language). Shows "19-oktabr 2026"; the calendar starts on Monday. Keys in the grid:
 * arrows (day/week), Home/End (week start/end), PageUp/PageDown (month), Shift+PageUp/PageDown
 * (year), Enter/Space choose, Escape closes. Values outside min/max are disabled.
 */
export function DatePicker({ value, onChange, min, max, today: todayProp, placeholder, clearable = false, variant = 'field', ...aria }: DatePickerProps) {
  const { t, locale } = useI18n()
  const uid = useId()
  const trigger = useRef<HTMLButtonElement>(null)
  const grid = useRef<HTMLTableElement>(null)
  const [open, setOpen] = useState(false)
  const [focus, setFocus] = useState('')
  const moveFocus = useRef(false)
  const today = todayProp ?? localDate(Date.now(), DEFAULT_TIME_ZONE)
  const valid = isValidDate(value) ? value : ''
  const inRange = (d: string) => (!min || d >= min) && (!max || d <= max)
  const clamp = (d: string) => (min && d < min ? min : max && d > max ? max : d)
  const dayId = (d: string) => `${uid}-d-${d}`
  const [pos, place] = usePlacement(trigger)

  function show() {
    place()
    setFocus(clamp(valid || today))
    moveFocus.current = true
    setOpen(true)
  }
  function close(restoreFocus: boolean) {
    setOpen(false)
    if (restoreFocus) trigger.current?.focus()
  }
  function choose(d: string) {
    if (!inRange(d)) return
    if (d !== value) onChange(d)
    close(true)
  }
  function go(d: string) {
    moveFocus.current = true
    setFocus(clamp(d))
  }

  useLayoutEffect(() => {
    if (!open || !moveFocus.current) return
    moveFocus.current = false
    document.getElementById(dayId(focus))?.focus()
    // dayId depends only on uid.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, focus])

  function onGridKey(e: KeyboardEvent<HTMLTableElement>) {
    const wd = weekdayOf(focus)
    const moves: Record<string, () => string> = {
      ArrowLeft: () => addDays(focus, -1),
      ArrowRight: () => addDays(focus, 1),
      ArrowUp: () => addDays(focus, -7),
      ArrowDown: () => addDays(focus, 7),
      Home: () => addDays(focus, 1 - wd),
      End: () => addDays(focus, 7 - wd),
      PageUp: () => addMonths(focus, e.shiftKey ? -12 : -1),
      PageDown: () => addMonths(focus, e.shiftKey ? 12 : 1),
    }
    const move = moves[e.key]
    if (!move) return
    e.preventDefault()
    go(move())
  }

  const month = focus.slice(0, 7) || today.slice(0, 7)
  const first = `${month}-01`
  const start = addDays(first, 1 - weekdayOf(first))
  const days = Array.from({ length: 42 }, (_, i) => addDays(start, i))
  const [y, m] = month.split('-').map(Number)
  const weekdays = weekdayNamesShort(locale)
  const titleId = `${uid}-title`

  return (
    <>
      <Trigger
        open={open}
        icon={<CalendarDays className="icon picker-trigger__icon" aria-hidden="true" />}
        text={valid ? formatDate(valid, locale) : undefined}
        placeholder={placeholder ?? t('date.choose')}
        variant={variant}
        triggerRef={trigger}
        onClick={() => (open ? close(false) : show())}
        aria={aria}
      />
      {open && (
        <PickerPopover anchor={trigger} pos={pos} onMove={place} label={t('date.choose')} onClose={close} className="picker-pop--date">
          <div className="picker-head">
            <button type="button" className="btn btn--ghost btn--small btn--icon" aria-label={t('date.prevMonth')} onClick={() => go(addMonths(focus, -1))}>
              <ChevronLeft className="icon" aria-hidden="true" />
            </button>
            <p className="picker-head__title" id={titleId} aria-live="polite">
              {capitalize(monthName(m ?? 1, locale, 'long'))} {y}
            </p>
            <button type="button" className="btn btn--ghost btn--small btn--icon" aria-label={t('date.nextMonth')} onClick={() => go(addMonths(focus, 1))}>
              <ChevronRight className="icon" aria-hidden="true" />
            </button>
          </div>
          <table ref={grid} role="grid" className="picker-grid" aria-labelledby={titleId} onKeyDown={onGridKey}>
            <thead>
              <tr>
                {weekdays.map((w) => (
                  <th key={w} scope="col">
                    {w}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {[0, 1, 2, 3, 4, 5].map((row) => (
                <tr key={row}>
                  {days.slice(row * 7, row * 7 + 7).map((d) => {
                    const outside = !d.startsWith(month)
                    return (
                      <td key={d} aria-selected={d === valid}>
                        <button
                          id={dayId(d)}
                          type="button"
                          tabIndex={d === focus ? 0 : -1}
                          disabled={!inRange(d)}
                          aria-label={formatDate(d, locale)}
                          aria-current={d === today ? 'date' : undefined}
                          className={`picker-day${outside ? ' picker-day--outside' : ''}${d === valid ? ' picker-day--selected' : ''}${d === today ? ' picker-day--today' : ''}`}
                          onClick={() => choose(d)}
                        >
                          {Number(d.slice(8))}
                        </button>
                      </td>
                    )
                  })}
                </tr>
              ))}
            </tbody>
          </table>
          <div className="picker-foot">
            <button type="button" className="btn btn--ghost btn--small" disabled={!inRange(today)} onClick={() => choose(today)}>
              {t('date.today')}
            </button>
            {clearable && valid && (
              <button
                type="button"
                className="btn btn--ghost btn--small"
                onClick={() => {
                  onChange('')
                  close(true)
                }}
              >
                {t('date.clear')}
              </button>
            )}
          </div>
        </PickerPopover>
      )}
    </>
  )
}

export interface MonthPickerProps extends AriaProps {
  /** "YYYY-MM" or "". */
  value: string
  onChange: (value: string) => void
  /** Branch-local current month (defaults to Asia/Tashkent). */
  thisMonth?: string
  variant?: 'field' | 'small'
}

/** Month picker (replaces <input type="month">): a year header and twelve months; arrows move, Enter chooses. */
export function MonthPicker({ value, onChange, thisMonth: current, variant = 'field', ...aria }: MonthPickerProps) {
  const { t, locale } = useI18n()
  const uid = useId()
  const trigger = useRef<HTMLButtonElement>(null)
  const [open, setOpen] = useState(false)
  const [focus, setFocus] = useState('')
  const moveFocus = useRef(false)
  const thisMonth = current ?? localDate(Date.now(), DEFAULT_TIME_ZONE).slice(0, 7)
  const valid = /^\d{4}-\d{2}$/.test(value) ? value : ''
  const monthId = (ym: string) => `${uid}-m-${ym}`
  const shift = (ym: string, n: number) => addMonths(`${ym}-01`, n).slice(0, 7)
  const [pos, place] = usePlacement(trigger)

  function show() {
    place()
    setFocus(valid || thisMonth)
    moveFocus.current = true
    setOpen(true)
  }
  function close(restoreFocus: boolean) {
    setOpen(false)
    if (restoreFocus) trigger.current?.focus()
  }
  function choose(ym: string) {
    if (ym !== value) onChange(ym)
    close(true)
  }
  function go(ym: string) {
    moveFocus.current = true
    setFocus(ym)
  }
  useLayoutEffect(() => {
    if (!open || !moveFocus.current) return
    moveFocus.current = false
    document.getElementById(monthId(focus))?.focus()
    // monthId depends only on uid.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, focus])

  function onKey(e: KeyboardEvent<HTMLDivElement>) {
    const moves: Record<string, number> = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -3, ArrowDown: 3, PageUp: -12, PageDown: 12 }
    const n = moves[e.key]
    if (n === undefined) return
    e.preventDefault()
    go(shift(focus, n))
  }

  const year = Number((focus || thisMonth).slice(0, 4))
  const display = valid ? `${capitalize(monthName(Number(valid.slice(5)), locale, 'long'))} ${valid.slice(0, 4)}` : undefined
  return (
    <>
      <Trigger
        open={open}
        icon={<CalendarDays className="icon picker-trigger__icon" aria-hidden="true" />}
        text={display}
        placeholder={t('date.chooseMonth')}
        variant={variant}
        triggerRef={trigger}
        onClick={() => (open ? close(false) : show())}
        aria={aria}
      />
      {open && (
        <PickerPopover anchor={trigger} pos={pos} onMove={place} label={t('date.chooseMonth')} onClose={close} className="picker-pop--month">
          <div className="picker-head">
            <button type="button" className="btn btn--ghost btn--small btn--icon" aria-label={t('date.prevYear')} onClick={() => go(shift(focus, -12))}>
              <ChevronLeft className="icon" aria-hidden="true" />
            </button>
            <p className="picker-head__title" aria-live="polite">
              {year}
            </p>
            <button type="button" className="btn btn--ghost btn--small btn--icon" aria-label={t('date.nextYear')} onClick={() => go(shift(focus, 12))}>
              <ChevronRight className="icon" aria-hidden="true" />
            </button>
          </div>
          <div className="picker-months" role="group" aria-label={String(year)} onKeyDown={onKey}>
            {Array.from({ length: 12 }, (_, i) => {
              const ym = `${year}-${pad(i + 1)}`
              return (
                <button
                  key={ym}
                  id={monthId(ym)}
                  type="button"
                  tabIndex={ym === focus ? 0 : -1}
                  aria-pressed={ym === valid}
                  aria-label={`${capitalize(monthName(i + 1, locale, 'long'))} ${year}`}
                  aria-current={ym === thisMonth ? 'date' : undefined}
                  className={`picker-month${ym === valid ? ' picker-day--selected' : ''}${ym === thisMonth ? ' picker-day--today' : ''}`}
                  onClick={() => choose(ym)}
                >
                  {capitalize(monthName(i + 1, locale, 'short'))}
                </button>
              )
            })}
          </div>
          <div className="picker-foot">
            <button type="button" className="btn btn--ghost btn--small" onClick={() => choose(thisMonth)}>
              {t('date.thisMonth')}
            </button>
          </div>
        </PickerPopover>
      )}
    </>
  )
}

export interface TimeSelectProps extends Omit<SelectProps, 'options' | 'placeholder'> {
  /** Minutes between options (default 15). */
  step?: number
  min?: string
  max?: string
}

/** "HH:mm" chooser on a fixed grid (replaces <input type="time">); typing "14" jumps to 14:00. */
export function TimeSelect({ step = 15, min = '00:00', max = '23:59', value, ...props }: TimeSelectProps) {
  const options = useMemo(() => {
    const list: string[] = []
    for (let m = toMinutes(min); m <= toMinutes(max); m += step) list.push(fromMinutes(m))
    if (value && !list.includes(value)) list.push(value)
    return list.sort().map((v) => ({ value: v, label: v }))
  }, [step, min, max, value])
  // A short visual placeholder; the meaning is in the label / aria-label.
  return <Select {...props} value={value} options={options} placeholder="––:––" />
}

/**
 * Date + time pair for a branch-local "YYYY-MM-DDTHH:mm" value (replaces <input type="datetime-local">).
 * The value is complete only when both parts are chosen; until then it is "".
 */
export function DateTimePicker({
  value,
  onChange,
  min,
  step = 15,
  ...aria
}: AriaProps & { value: string; onChange: (value: string) => void; min?: string; step?: number }) {
  const { t } = useI18n()
  const parse = (v: string) => ({ date: v.slice(0, 10), time: v.slice(11, 16) })
  const [draft, setDraft] = useState(() => parse(value))
  // A value set from outside (e.g. reset to "" after saving) replaces the half-filled draft.
  const [seen, setSeen] = useState(value)
  if (value !== seen) {
    setSeen(value)
    setDraft(parse(value))
  }
  const shown = value ? parse(value) : draft
  const update = (patch: Partial<typeof draft>) => {
    const next = { ...shown, ...patch }
    setDraft(next)
    onChange(isValidDate(next.date) && /^\d{2}:\d{2}$/.test(next.time) ? `${next.date}T${next.time}` : '')
  }
  return (
    <div className="datetime" role="group" aria-labelledby={aria['aria-labelledby'] ?? (aria.id ? `${aria.id}-label` : undefined)}>
      <DatePicker {...aria} value={shown.date} min={min?.slice(0, 10)} onChange={(date) => update({ date })} />
      <span className="datetime__time">
        <Clock className="icon" aria-hidden="true" />
        <TimeSelect aria-label={t('date.chooseTime')} aria-invalid={aria['aria-invalid']} step={step} value={shown.time} onChange={(time) => update({ time })} />
      </span>
    </div>
  )
}

/** Label + DatePicker for filters (the label is a real <label for>). */
export function DateField({ label, inline = false, ...props }: DatePickerProps & { label: ReactNode; inline?: boolean }) {
  const auto = useId()
  const id = props.id ?? `${auto}-date`
  return (
    <div className={inline ? 'field field--inline' : 'field'}>
      <label id={`${id}-label`} htmlFor={id}>
        {label}
      </label>
      <DatePicker {...props} id={id} variant={props.variant ?? (inline ? 'small' : 'field')} />
    </div>
  )
}

/** Label + MonthPicker. */
export function MonthField({ label, inline = false, ...props }: MonthPickerProps & { label: ReactNode; inline?: boolean }) {
  const auto = useId()
  const id = props.id ?? `${auto}-month`
  return (
    <div className={inline ? 'field field--inline' : 'field'}>
      <label id={`${id}-label`} htmlFor={id}>
        {label}
      </label>
      <MonthPicker {...props} id={id} variant={props.variant ?? (inline ? 'small' : 'field')} />
    </div>
  )
}
