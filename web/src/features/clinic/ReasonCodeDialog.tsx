import { useState, type FormEvent } from 'react'
import { useI18n } from '../../i18n/context'
import { useAction } from '../../hooks/useAction'
import { Dialog } from '../../ui/Dialog'
import { Select } from '../../ui/Select'
import { Field, FormError, SubmitButton } from '../../ui/forms'
import { reasonLabel } from '../appointments/format'

/** Picks one of the predefined reason codes (reject / clinic cancel) and runs the action. */
export function ReasonCodeDialog({
  title,
  codes,
  submitLabel,
  onSubmit,
  onDone,
  onError,
  onClose,
}: {
  title: string
  codes: readonly string[]
  submitLabel: string
  onSubmit: (code: string) => Promise<unknown>
  onDone: () => void
  /** Lets the caller react to stale-state errors (e.g. refresh the list). */
  onError?: (error: unknown) => void
  onClose: () => void
}) {
  const { t } = useI18n()
  const [code, setCode] = useState('')
  const [touched, setTouched] = useState(false)
  const action = useAction(onSubmit)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setTouched(true)
    if (!code) return
    const r = await action.run(code)
    if (r.ok) onDone()
    else onError?.(r.error)
  }

  return (
    <Dialog title={title} onClose={onClose}>
      <form className="stack" onSubmit={submit} noValidate>
        <Field label={t('common.reason')} error={touched && !code ? t('valid.required') : null}>
          {(p) => (
            <Select {...p} value={code} onChange={setCode} options={[{ value: '', label: t('req.chooseReason') }, ...codes.map((c) => ({ value: c, label: reasonLabel(t, c) }))]} />
          )}
        </Field>
        <FormError error={action.error} />
        <div className="row">
          <SubmitButton pending={action.pending} className="btn btn--danger">
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
