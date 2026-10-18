import { useState, type FormEvent } from 'react'
import { useApi } from '../../api/context'
import type { ClinicProfileDto, RegisterClinicBody } from '../../api/types'
import { useSession } from '../../auth/session'
import { useAction } from '../../hooks/useAction'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { isValidUzPhone, normalizePhone } from '../../lib/phone'
import { FormError, Notice, SubmitButton } from '../../ui/forms'
import { QueryView } from '../../ui/states'
import { Badge, PageHeader } from '../../ui/widgets'
import { useClinic } from './clinicContext'
import { ClinicProfileFields } from './ClinicProfileFields'
import { validateClinicProfile } from './clinicProfile'
import { Perm } from './permissions'

export function ClinicProfilePage() {
  const { t } = useI18n()
  useTitle(t('clinicNav.profile'))
  const api = useApi()
  const { clinicId } = useClinic()
  const profile = useQuery(`clinic-profile:${clinicId}`, () => api.clinic.profile(clinicId))
  return (
    <div className="stack">
      <PageHeader title={t('clinicNav.profile')} />
      <QueryView query={profile}>{(p) => <ProfileForm key={`${p.id}:${p.name}:${p.phone}`} profile={p} onSaved={profile.reload} />}</QueryView>
    </div>
  )
}

function ProfileForm({ profile: p, onSaved }: { profile: ClinicProfileDto; onSaved: () => void }) {
  const { t } = useI18n()
  const api = useApi()
  const { clinicId, can } = useClinic()
  const { refresh } = useSession()
  const [value, setValue] = useState<RegisterClinicBody>({ name: p.name, legalName: p.legalName, description: p.description, phone: p.phone })
  const [touched, setTouched] = useState(false)
  const [saved, setSaved] = useState(false)
  const save = useAction((body: RegisterClinicBody) => api.clinic.updateProfile(clinicId, body))
  const editable = can(Perm.profileManage)
  const errors = validateClinicProfile(value, t, isValidUzPhone)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setTouched(true)
    setSaved(false)
    if (Object.values(errors).some(Boolean)) return
    const r = await save.run({ ...value, phone: normalizePhone(value.phone) })
    if (r.ok) {
      setSaved(true)
      await refresh()
      onSaved()
    }
  }

  return (
    <form className="card stack" onSubmit={submit} noValidate>
      <p>
        {t('common.status')}: <Badge tone={p.verificationStatus === 'Approved' ? 'success' : 'warning'}>{t(`verification.${p.verificationStatus}`)}</Badge>
      </p>
      <ClinicProfileFields value={value} onChange={setValue} errors={touched ? errors : {}} disabled={!editable} />
      <FormError error={save.error} />
      {saved && <Notice>{t('common.saved')}</Notice>}
      {editable ? <SubmitButton pending={save.pending}>{t('common.save')}</SubmitButton> : <p className="muted small">{t('profile.readOnly')}</p>}
    </form>
  )
}
