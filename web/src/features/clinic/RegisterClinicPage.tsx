import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router'
import { useApi } from '../../api/context'
import type { RegisterClinicBody } from '../../api/types'
import { useSession } from '../../auth/session'
import { useAction } from '../../hooks/useAction'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { isValidUzPhone, normalizePhone } from '../../lib/phone'
import { FormError, SubmitButton } from '../../ui/forms'
import { PageHeader } from '../../ui/widgets'
import { ClinicProfileFields } from './ClinicProfileFields'
import { validateClinicProfile } from './clinicProfile'

export function RegisterClinicPage() {
  const { t } = useI18n()
  useTitle(t('register.title'))
  const api = useApi()
  const navigate = useNavigate()
  const { refresh } = useSession()
  const [value, setValue] = useState<RegisterClinicBody>({ name: '', legalName: '', description: '', phone: '' })
  const [touched, setTouched] = useState(false)
  const register = useAction((body: RegisterClinicBody) => api.clinic.register(body))
  const errors = validateClinicProfile(value, t, isValidUzPhone)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setTouched(true)
    if (Object.values(errors).some(Boolean)) return
    const r = await register.run({ ...value, name: value.name.trim(), legalName: value.legalName.trim(), description: value.description.trim(), phone: value.phone.trim() ? normalizePhone(value.phone) : '' })
    if (r.ok) {
      await refresh()
      navigate(`/clinic/${r.value.id}/branches`, { replace: true })
    }
  }

  return (
    <div className="page narrow stack">
      <PageHeader title={t('register.title')} subtitle={t('register.intro')} />
      <form className="card stack" onSubmit={submit} noValidate>
        <ClinicProfileFields value={value} onChange={setValue} errors={touched ? errors : {}} />
        <ol className="notes">
          <li>{t('register.step1')}</li>
          <li>{t('register.step2')}</li>
          <li>{t('register.step3')}</li>
        </ol>
        <FormError error={register.error} />
        <SubmitButton pending={register.pending}>{t('register.submit')}</SubmitButton>
      </form>
    </div>
  )
}
