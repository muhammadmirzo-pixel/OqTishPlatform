import { ChevronDown, type LucideIcon } from 'lucide-react'
import { useState } from 'react'

/**
 * Working-context switch (clinic, branch) shown as a pill: icon, label, current value. A native
 * select underneath keeps keyboard and screen-reader behaviour; a user change pulses the pill once
 * so the switch is noticed (no pulse on first render).
 */
export function ContextChip({
  icon: Icon,
  label,
  value,
  options,
  onChange,
}: {
  icon: LucideIcon
  label: string
  value: string
  options: { value: string; label: string }[]
  onChange: (value: string) => void
}) {
  const [pulse, setPulse] = useState(0)
  return (
    <label className="context-chip">
      <Icon className="icon context-chip__icon" aria-hidden="true" />
      <span className="context-chip__label">{label}</span>
      <select
        className="context-chip__select"
        value={value}
        onChange={(e) => {
          setPulse((n) => n + 1)
          onChange(e.target.value)
        }}
      >
        {options.map((o) => (
          <option key={o.value} value={o.value}>
            {o.label}
          </option>
        ))}
      </select>
      <ChevronDown className="icon context-chip__chev" aria-hidden="true" />
      {pulse > 0 && <span key={pulse} className="context-chip__pulse" aria-hidden="true" />}
    </label>
  )
}
