import type { LucideIcon } from 'lucide-react'
import { useId, useState } from 'react'
import { Select } from './Select'

/**
 * Working-context switch (clinic, branch) shown as a pill: icon, label, current value. The value is
 * a bare Select (combobox + listbox, keyboard and screen-reader friendly); a user change pulses the
 * pill once so the switch is noticed (no pulse on first render).
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
  const id = useId()
  return (
    <div className="context-chip">
      <Icon className="icon context-chip__icon" aria-hidden="true" />
      <label className="context-chip__label" id={`${id}-label`} htmlFor={id}>
        {label}
      </label>
      <Select
        id={id}
        variant="bare"
        className="context-chip__select"
        value={value}
        onChange={(v) => {
          setPulse((n) => n + 1)
          onChange(v)
        }}
        options={options}
      />
      {pulse > 0 && <span key={pulse} className="context-chip__pulse" aria-hidden="true" />}
    </div>
  )
}
