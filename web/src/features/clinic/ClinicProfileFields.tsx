import type { RegisterClinicBody } from '../../api/types'
import { useI18n } from '../../i18n/context'
import { Field } from '../../ui/forms'
import type { ClinicProfileErrors as Errors } from './clinicProfile'

export function ClinicProfileFields({ value, onChange, errors, disabled }: { value: RegisterClinicBody; onChange: (v: RegisterClinicBody) => void; errors: Errors; disabled?: boolean }) {
  const { t } = useI18n()
  const set = (k: keyof RegisterClinicBody) => (e: { target: { value: string } }) => onChange({ ...value, [k]: e.target.value })
  return (
    <>
      <Field label={t('register.name')} hint={t('register.nameHint')} error={errors.name}>
        {(p) => <input {...p} disabled={disabled} maxLength={200} value={value.name} onChange={set('name')} />}
      </Field>
      <Field label={t('register.legalName')} hint={t('register.legalNameHint')} error={errors.legalName} optional>
        {(p) => <input {...p} disabled={disabled} maxLength={300} value={value.legalName} onChange={set('legalName')} />}
      </Field>
      <Field label={t('common.phone')} error={errors.phone} optional>
        {(p) => <input {...p} disabled={disabled} type="tel" inputMode="tel" placeholder="+998 71 123 45 67" value={value.phone} onChange={set('phone')} />}
      </Field>
      <Field label={t('register.description')} hint={t('register.descriptionHint')} error={errors.description} optional>
        {(p) => <textarea {...p} disabled={disabled} rows={4} maxLength={4000} value={value.description} onChange={set('description')} />}
      </Field>
    </>
  )
}
