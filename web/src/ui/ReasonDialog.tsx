import { useState, type FormEvent, type ReactNode } from 'react'
import { useI18n } from '../i18n/context'
import { useAction } from '../hooks/useAction'
import { Dialog } from './Dialog'
import { Field, FormError, SubmitButton } from './forms'

/**
 * Asks for a free-text reason (or other single text value) and runs the action with it.
 * Closes on success; keeps the dialog open with a localised error otherwise.
 */
export function ReasonDialog({
  title,
  label,
  submitLabel,
  intro,
  maxLength = 500,
  minLength = 3,
  multiline = true,
  onSubmit,
  onDone,
  onClose,
}: {
  title: string
  label: string
  submitLabel: string
  intro?: ReactNode
  maxLength?: number
  minLength?: number
  multiline?: boolean
  onSubmit: (value: string) => Promise<unknown>
  onDone: () => void
  onClose: () => void
}) {
  const { t } = useI18n()
  const [value, setValue] = useState('')
  const [touched, setTouched] = useState(false)
  const action = useAction(onSubmit)
  const trimmed = value.trim()
  const invalid = trimmed.length < minLength ? t('valid.minLength', { min: minLength }) : trimmed.length > maxLength ? t('valid.maxLength', { max: maxLength }) : null

  async function submit(e: FormEvent) {
    e.preventDefault()
    setTouched(true)
    if (invalid) return
    const result = await action.run(trimmed)
    if (result.ok) onDone()
  }

  return (
    <Dialog title={title} onClose={onClose}>
      <form onSubmit={submit} noValidate className="stack">
        {intro}
        <Field label={label} error={touched ? invalid : null}>
          {(p) =>
            multiline ? (
              <textarea {...p} rows={3} value={value} maxLength={maxLength} onChange={(e) => setValue(e.target.value)} />
            ) : (
              <input {...p} value={value} maxLength={maxLength} onChange={(e) => setValue(e.target.value)} />
            )
          }
        </Field>
        <FormError error={action.error} />
        <div className="row">
          <SubmitButton pending={action.pending}>{submitLabel}</SubmitButton>
          <button type="button" className="btn btn--ghost" onClick={onClose}>
            {t('common.cancel')}
          </button>
        </div>
      </form>
    </Dialog>
  )
}

/** Yes/no confirmation that runs an action; shows a localised error on failure. */
export function ConfirmDialog({
  title,
  body,
  confirmLabel,
  danger,
  confirmDisabled,
  onConfirm,
  onDone,
  onClose,
}: {
  title: string
  body: ReactNode
  confirmLabel: string
  danger?: boolean
  confirmDisabled?: boolean
  onConfirm: () => Promise<unknown>
  onDone: () => void
  onClose: () => void
}) {
  const { t } = useI18n()
  const action = useAction(onConfirm)
  return (
    <Dialog title={title} onClose={onClose}>
      <div className="stack">
        <div>{body}</div>
        <FormError error={action.error} />
        <div className="row">
          <button
            type="button"
            className={danger ? 'btn btn--danger' : 'btn'}
            disabled={action.pending || confirmDisabled}
            onClick={async () => {
              const r = await action.run()
              if (r.ok) onDone()
            }}
          >
            {action.pending ? t('common.saving') : confirmLabel}
          </button>
          <button type="button" className="btn btn--ghost" onClick={onClose}>
            {t('common.cancel')}
          </button>
        </div>
      </div>
    </Dialog>
  )
}
