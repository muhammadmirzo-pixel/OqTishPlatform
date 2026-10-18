import { useId, type ReactNode } from 'react'
import { ApiError } from '../api/client'
import { errorMessage } from '../api/errors'
import { useI18n } from '../i18n/context'

interface FieldProps {
  label: ReactNode
  hint?: ReactNode
  error?: string | null
  optional?: boolean
  className?: string
  children: (props: { id: string; 'aria-describedby'?: string; 'aria-invalid'?: true }) => ReactNode
}

/** Label + control + hint + error, wired with ids for screen readers. */
export function Field({ label, hint, error, optional, className, children }: FieldProps) {
  const { t } = useI18n()
  const id = useId()
  const hintId = hint ? `${id}-hint` : undefined
  const errorId = error ? `${id}-error` : undefined
  const describedBy = [hintId, errorId].filter(Boolean).join(' ') || undefined
  return (
    <div className={`field${error ? ' field--invalid' : ''}${className ? ` ${className}` : ''}`}>
      <label htmlFor={id}>
        {label}
        {optional && <span className="muted"> ({t('common.optional')})</span>}
      </label>
      {children({ id, 'aria-describedby': describedBy, ...(error ? { 'aria-invalid': true as const } : {}) })}
      {hint && (
        <p id={hintId} className="field__hint">
          {hint}
        </p>
      )}
      {error && (
        <p id={errorId} className="field__error">
          {error}
        </p>
      )}
    </div>
  )
}

/** Localised error banner for a failed action; includes server field errors and the trace id. */
export function FormError({ error }: { error: unknown }) {
  const { t } = useI18n()
  if (error === undefined || error === null) return null
  const fields = error instanceof ApiError ? Object.entries(error.fieldErrors) : []
  const traceId = error instanceof ApiError && error.kind === 'server' ? error.traceId : undefined
  return (
    <div className="alert alert--error" role="alert">
      <p>{errorMessage(t, error)}</p>
      {fields.length > 0 && (
        <ul>
          {fields.map(([field, messages]) => (
            <li key={field}>
              <strong>{field}</strong>: {messages.join(' ')}
            </li>
          ))}
        </ul>
      )}
      {traceId && <p className="muted small">{t('state.errorTrace', { traceId })}</p>}
    </div>
  )
}

/** Polite live region for async results ("Saved", "Request sent"). */
export function Notice({ tone = 'success', children }: { tone?: 'success' | 'info' | 'warning'; children: ReactNode }) {
  return (
    <div className={`alert alert--${tone}`} role="status" aria-live="polite">
      {children}
    </div>
  )
}

export function SubmitButton({ pending, children, className = 'btn', disabled }: { pending: boolean; children: ReactNode; className?: string; disabled?: boolean }) {
  const { t } = useI18n()
  return (
    <button type="submit" className={className} disabled={pending || disabled} aria-busy={pending || undefined}>
      {pending ? t('common.saving') : children}
    </button>
  )
}

export function Checkbox({ label, checked, onChange, disabled }: { label: ReactNode; checked: boolean; onChange: (v: boolean) => void; disabled?: boolean }) {
  return (
    <label className="check">
      <input type="checkbox" checked={checked} disabled={disabled} onChange={(e) => onChange(e.target.checked)} />
      <span>{label}</span>
    </label>
  )
}

/** Accessible group of checkboxes for picking ids (branches, services). */
export function CheckboxGroup({
  legend,
  options,
  value,
  onChange,
  error,
}: {
  legend: ReactNode
  options: { id: string; label: string }[]
  value: string[]
  onChange: (next: string[]) => void
  error?: string | null
}) {
  return (
    <fieldset className={`fieldset${error ? ' field--invalid' : ''}`}>
      <legend>{legend}</legend>
      <div className="check-grid">
        {options.map((o) => (
          <Checkbox
            key={o.id}
            label={o.label}
            checked={value.includes(o.id)}
            onChange={(on) => onChange(on ? [...value, o.id] : value.filter((v) => v !== o.id))}
          />
        ))}
      </div>
      {error && <p className="field__error">{error}</p>}
    </fieldset>
  )
}
