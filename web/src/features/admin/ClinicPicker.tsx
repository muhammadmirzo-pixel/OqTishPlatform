import { useState } from 'react'
import { useApi } from '../../api/context'
import { useSession } from '../../auth/session'
import { useQuery } from '../../hooks/useQuery'
import { useI18n } from '../../i18n/context'
import { PlatformPerm } from '../clinic/permissions'

/** Choose a clinic by searching the admin clinic list; falls back to a raw id when not permitted. */
export function ClinicPicker({ value, onChange, label }: { value: string; onChange: (id: string) => void; label?: string }) {
  const { t } = useI18n()
  const api = useApi()
  const { me } = useSession()
  const canList = me?.platformPermissions.includes(PlatformPerm.clinicVerify) ?? false
  const [q, setQ] = useState('')
  const clinics = useQuery(canList ? `admin-clinic-picker:${q}` : null, () => api.admin.clinics({ q: q || undefined, page: 1 }))

  if (!canList) {
    return (
      <label className="field">
        <span>{label ?? t('adm.clinicId')}</span>
        <input value={value} onChange={(e) => onChange(e.target.value.trim())} />
      </label>
    )
  }
  return (
    <div className="row">
      <label className="field">
        <span>{t('adm.searchClinic')}</span>
        <input type="search" value={q} onChange={(e) => setQ(e.target.value)} />
      </label>
      <label className="field">
        <span>{label ?? t('common.clinic')}</span>
        <select value={value} onChange={(e) => onChange(e.target.value)}>
          <option value="">{t('adm.chooseClinic')}</option>
          {clinics.data?.items.map((c) => (
            <option key={c.id} value={c.id}>
              {c.name}
            </option>
          ))}
        </select>
      </label>
    </div>
  )
}
