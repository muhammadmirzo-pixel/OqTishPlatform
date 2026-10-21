import { ChevronRight, ListChecks, Plus, Stethoscope, Store } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { useApi } from '../../api/context'
import type { DoctorBody, DoctorDto, ServiceDto, StaffMemberDto } from '../../api/types'
import { useSession } from '../../auth/session'
import { useAction } from '../../hooks/useAction'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { Dialog } from '../../ui/Dialog'
import { Select } from '../../ui/Select'
import { Checkbox, CheckboxGroup, Field, FormError, SubmitButton } from '../../ui/forms'
import { Monogram } from '../../ui/Monogram'
import { EmptyState, QueryView } from '../../ui/states'
import { useToast } from '../../ui/toastContext'
import { Badge, PageHeader } from '../../ui/widgets'
import { useClinic } from './clinicContext'
import { Perm } from './permissions'
import { formatPhone } from '../../lib/phone'

export function DoctorsPage() {
  const { t } = useI18n()
  useTitle(t('clinicNav.doctors'))
  const api = useApi()
  const { me } = useSession()
  const { clinicId, can, branchName } = useClinic()
  const doctors = useQuery(`doctors:${clinicId}`, () => api.clinic.doctors(clinicId))
  const services = useQuery(`services:${clinicId}`, () => api.clinic.services(clinicId))
  const [creating, setCreating] = useState(false)
  const toast = useToast()
  const manage = can(Perm.scheduleManage)
  const serviceName = (id: string) => services.data?.find((s) => s.id === id)?.name ?? '…'

  return (
    <div className="stack">
      <PageHeader
        icon={Stethoscope}
        title={t('clinicNav.doctors')}
        actions={
          manage && (
            <button type="button" className="btn" onClick={() => setCreating(true)}>
              <Plus className="icon" aria-hidden="true" />
              {t('doc.add')}
            </button>
          )
        }
      />
      <QueryView query={doctors}>
        {(list) => {
          const visible = manage || can(Perm.catalogManage) ? list : list.filter((d) => d.userId === me?.id)
          return visible.length === 0 ? (
            <EmptyState title={t('doc.none')} icon={Stethoscope} />
          ) : (
            <ul className="list-plain stack">
              {visible.map((d) => (
                <li key={d.id}>
                  <Link to={`/clinic/${clinicId}/doctors/${d.id}`} className="card card--link entity">
                    <Monogram name={d.displayName} seed={d.id} imageId={d.avatarId} size={44} round={!!d.avatarId} />
                    <span className="entity__body">
                      <span className="entity__title">
                        <strong>{d.displayName}</strong> {!d.isActive && <Badge>{t('common.inactive')}</Badge>} {d.userId === me?.id && <Badge tone="info">{t('doc.you')}</Badge>}
                      </span>
                      <span className="entity__facts">
                        <span className="entity__fact">
                          <Store className="icon" aria-hidden="true" />
                          {d.branchIds.map(branchName).join(', ') || t('doc.noBranches')}
                        </span>
                        <span className="entity__fact">
                          <ListChecks className="icon" aria-hidden="true" />
                          {d.serviceIds.map(serviceName).join(', ') || t('doc.noServices')}
                        </span>
                      </span>
                    </span>
                    <ChevronRight className="icon entity__chev" aria-hidden="true" />
                  </Link>
                </li>
              ))}
            </ul>
          )
        }}
      </QueryView>
      {creating && services.data && (
        <DoctorDialog
          doctor={null}
          services={services.data}
          onDone={() => {
            setCreating(false)
            toast({ title: t('common.saved') })
            doctors.reload()
          }}
          onClose={() => setCreating(false)}
        />
      )}
    </div>
  )
}

export function DoctorDialog({ doctor, services, onDone, onClose }: { doctor: DoctorDto | null; services: ServiceDto[]; onDone: () => void; onClose: () => void }) {
  const { t } = useI18n()
  const api = useApi()
  const { clinicId, branches, can } = useClinic()
  // Linking the record to an account grants Doctor-own access, so changing `userId` also needs
  // staff.manage (DECISIONS.md BE1b-6). Without it the current link is sent back unchanged — the API
  // treats a missing/null userId as "unlink".
  const canLink = can(Perm.staffManage)
  const staff = useQuery(canLink ? `staff:${clinicId}` : null, () => api.clinic.staff(clinicId))
  const [name, setName] = useState(doctor?.displayName ?? '')
  const [isActive, setActive] = useState(doctor?.isActive ?? true)
  const [branchIds, setBranchIds] = useState<string[]>(doctor?.branchIds ?? [])
  const [serviceIds, setServiceIds] = useState<string[]>(doctor?.serviceIds ?? [])
  const [userId, setUserId] = useState(doctor?.userId ?? '')
  const [touched, setTouched] = useState(false)
  const save = useAction((body: DoctorBody) => (doctor ? api.clinic.updateDoctor(clinicId, doctor.id, body) : api.clinic.createDoctor(clinicId, body)))
  // Any non-removed member may be linked; Doctor-role members are listed first.
  const linkable: StaffMemberDto[] = [...(staff.data ?? [])].sort((a, b) => Number(b.role === 'Doctor') - Number(a.role === 'Doctor') || a.displayName.localeCompare(b.displayName))
  const currentLinkHidden = !!doctor?.userId && !linkable.some((s) => s.userId === doctor.userId)
  // Branch links outside the caller's scope are invisible here; keep them instead of silently dropping them.
  const hiddenBranchIds = (doctor?.branchIds ?? []).filter((id) => !branches.some((b) => b.id === id))
  const errors = {
    name: !name.trim() ? t('valid.required') : name.trim().length > 200 ? t('valid.maxLength', { max: 200 }) : null,
    branches: branchIds.length === 0 ? t('valid.chooseOne') : null,
    services: serviceIds.length === 0 ? t('valid.chooseOne') : null,
  }

  async function submit(e: FormEvent) {
    e.preventDefault()
    setTouched(true)
    if (errors.name || errors.branches || errors.services) return
    const r = await save.run({
      displayName: name.trim(),
      isActive,
      branchIds: [...branchIds, ...hiddenBranchIds],
      serviceIds,
      userId: canLink ? userId || null : (doctor?.userId ?? null),
    })
    if (r.ok) onDone()
  }

  return (
    <Dialog title={doctor ? t('doc.edit') : t('doc.add')} onClose={onClose}>
      <form className="stack" onSubmit={submit} noValidate>
        <Field label={t('doc.name')} error={touched ? errors.name : null}>
          {(p) => <input {...p} maxLength={200} value={name} onChange={(e) => setName(e.target.value)} />}
        </Field>
        <CheckboxGroup legend={t('doc.branches')} options={branches.map((b) => ({ id: b.id, label: b.name }))} value={branchIds} onChange={setBranchIds} error={touched ? errors.branches : null} />
        <CheckboxGroup legend={t('doc.services')} options={services.filter((s) => s.isActive).map((s) => ({ id: s.id, label: s.name }))} value={serviceIds} onChange={setServiceIds} error={touched ? errors.services : null} />
        {canLink && staff.data && (
          <Field label={t('doc.account')} hint={t('doc.accountHint')} optional>
            {(p) => (
              <Select
                {...p}
                value={userId}
                onChange={setUserId}
                options={[
                  { value: '', label: t('doc.noAccount') },
                  ...(currentLinkHidden ? [{ value: doctor!.userId!, label: t('doc.currentAccount') }] : []),
                  ...linkable.map((s) => ({ value: s.userId, label: `${s.displayName}${s.phoneMasked ? ` (${formatPhone(s.phoneMasked)})` : ''} · ${t(`role.${s.role}`)}` })),
                ]}
              />
            )}
          </Field>
        )}
        <Checkbox label={t('common.active')} checked={isActive} onChange={setActive} />
        <FormError error={save.error} />
        <SubmitButton pending={save.pending}>{t('common.save')}</SubmitButton>
      </form>
    </Dialog>
  )
}
