import { AlertTriangle } from 'lucide-react'
import { useState, type FormEvent, type ReactNode } from 'react'
import { useI18n } from '../i18n/context'
import { useAction } from '../hooks/useAction'
import { Dialog } from './Dialog'
import { Checkbox, Field, FormError, SubmitButton } from './forms'

/** Warning strip on top of a dangerous dialog: icon + the caller's explanation. */
export function DangerNote({ children }: { children: ReactNode }) {
  return (
    <div className="danger-note">
      <AlertTriangle className="icon" aria-hidden="true" />
      <div>{children}</div>
    </div>
  )
}

/**
 * Asks for a free-text reason (or other single text value) and runs the action with it.
 * Closes on success; keeps the dialog open with a localised error otherwise. `danger` marks a
 * destructive action: red frame, warning strip around `intro`, red submit button.
 */
export function ReasonDialog({
  title,
  label,
  submitLabel,
  intro,
  danger = false,
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
  danger?: boolean
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
    <Dialog title={title} onClose={onClose} tone={danger ? 'danger' : undefined}>
      <form onSubmit={submit} noValidate className="stack">
        {danger && intro ? <DangerNote>{intro}</DangerNote> : intro}
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
          <SubmitButton pending={action.pending} className={danger ? 'btn btn--danger' : 'btn'}>
            {submitLabel}
          </SubmitButton>
          <button type="button" className="btn btn--ghost" onClick={onClose}>
            {t('common.cancel')}
          </button>
        </div>
      </form>
    </Dialog>
  )
}

/**
 * Yes/no confirmation that runs an action; shows a localised error on failure. `danger` styles it as
 * destructive; `acknowledge` adds a checkbox that must be ticked first (for irreversible actions).
 */
export function ConfirmDialog({
  title,
  body,
  confirmLabel,
  danger,
  acknowledge,
  confirmDisabled,
  onConfirm,
  onDone,
  onClose,
}: {
  title: string
  body: ReactNode
  confirmLabel: string
  danger?: boolean
  acknowledge?: string
  confirmDisabled?: boolean
  onConfirm: () => Promise<unknown>
  onDone: () => void
  onClose: () => void
}) {
  const { t } = useI18n()
  const action = useAction(onConfirm)
  const [acknowledged, setAcknowledged] = useState(false)
  return (
    <Dialog title={title} onClose={onClose} tone={danger ? 'danger' : undefined}>
      <div className="stack">
        {danger ? <DangerNote>{body}</DangerNote> : <div>{body}</div>}
        {acknowledge && <Checkbox label={acknowledge} checked={acknowledged} onChange={setAcknowledged} />}
        <FormError error={action.error} />
        <div className="row">
          <button
            type="button"
            className={danger ? 'btn btn--danger' : 'btn'}
            disabled={action.pending || confirmDisabled || (!!acknowledge && !acknowledged)}
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
