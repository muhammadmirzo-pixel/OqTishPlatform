import { Clock, MapPin, Pencil, Phone, Plus, Store } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { useApi } from '../../api/context'
import { branchTimeZones, type BranchBody, type BranchDto } from '../../api/types'
import { useAction } from '../../hooks/useAction'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { isValidUzPhone, normalizePhone } from '../../lib/phone'
import { DEFAULT_TIME_ZONE } from '../../lib/time'
import { Dialog } from '../../ui/Dialog'
import { Checkbox, Field, FormError, SubmitButton } from '../../ui/forms'
import { EmptyState } from '../../ui/states'
import { useToast } from '../../ui/toastContext'
import { Badge, PageHeader } from '../../ui/widgets'
import { useClinic } from './clinicContext'
import { Perm } from './permissions'
import { WeeklyHoursEditor } from './WeeklyHoursEditor'
import { hasOverlap, intervalError, type Interval } from './hours'

export function BranchesPage() {
  const { t } = useI18n()
  useTitle(t('clinicNav.branches'))
  const { branches, can, canClinicWide, reloadBranches } = useClinic()
  const [editing, setEditing] = useState<BranchDto | 'new' | null>(null)
  const [hoursFor, setHoursFor] = useState<BranchDto | null>(null)
  const toast = useToast()
  // Creating a branch needs branch.manage clinic-wide; editing needs it on that branch (BE1a-8).
  const manage = can(Perm.branchManage)
  const create = canClinicWide(Perm.branchManage)

  return (
    <div className="stack">
      <PageHeader
        icon={Store}
        title={t('clinicNav.branches')}
        actions={
          create && (
            <button type="button" className="btn" onClick={() => setEditing('new')}>
              <Plus className="icon" aria-hidden="true" />
              {t('branch.add')}
            </button>
          )
        }
      />
      {branches.length === 0 ? (
        <EmptyState title={t('branch.none')} icon={Store} />
      ) : (
        <ul className="list-plain stack">
          {branches.map((b) => (
            <li key={b.id} className="card entity">
              <span className="icon-tile">
                <Store className="icon" aria-hidden="true" />
              </span>
              <div className="entity__body">
                <h2 className="entity__title">
                  {b.name} {!b.isActive && <Badge>{t('common.inactive')}</Badge>}
                </h2>
                <ul className="entity__facts">
                  <li>
                    <MapPin className="icon" aria-hidden="true" />
                    {b.district}, {b.address}
                  </li>
                  {b.phone && (
                    <li>
                      <Phone className="icon" aria-hidden="true" />
                      <span className="nowrap">{b.phone}</span>
                    </li>
                  )}
                  <li>
                    <Clock className="icon" aria-hidden="true" />
                    <span>
                      {b.hours.length === 0
                        ? t('branch.noHours')
                        : [...b.hours]
                            .sort((x, y) => x.weekday - y.weekday)
                            .map((h) => `${t(`weekday.${h.weekday}`)} ${h.open}–${h.close}`)
                            .join(' · ')}{' '}
                      <span className="muted">({b.timeZone})</span>
                    </span>
                  </li>
                </ul>
              </div>
              {manage && (
                <div className="entity__actions">
                  <button type="button" className="btn btn--ghost btn--small" onClick={() => setEditing(b)}>
                    <Pencil className="icon" aria-hidden="true" />
                    {t('common.edit')}
                  </button>
                  <button type="button" className="btn btn--ghost btn--small" onClick={() => setHoursFor(b)}>
                    <Clock className="icon" aria-hidden="true" />
                    {t('branch.hours')}
                  </button>
                </div>
              )}
            </li>
          ))}
        </ul>
      )}
      {editing && (
        <BranchDialog
          branch={editing === 'new' ? null : editing}
          onDone={() => {
            setEditing(null)
            toast({ title: t('common.saved') })
            reloadBranches()
          }}
          onClose={() => setEditing(null)}
        />
      )}
      {hoursFor && (
        <HoursDialog
          branch={hoursFor}
          onDone={() => {
            setHoursFor(null)
            toast({ title: t('common.saved') })
            reloadBranches()
          }}
          onClose={() => setHoursFor(null)}
        />
      )}
    </div>
  )
}

function BranchDialog({ branch, onDone, onClose }: { branch: BranchDto | null; onDone: () => void; onClose: () => void }) {
  const { t } = useI18n()
  const api = useApi()
  const { clinicId } = useClinic()
  const [v, setV] = useState({
    name: branch?.name ?? '',
    district: branch?.district ?? '',
    address: branch?.address ?? '',
    phone: branch?.phone ?? '',
    timeZone: branch && (branchTimeZones as readonly string[]).includes(branch.timeZone) ? branch.timeZone : DEFAULT_TIME_ZONE,
    latitude: branch?.latitude?.toString() ?? '',
    longitude: branch?.longitude?.toString() ?? '',
    isActive: branch?.isActive ?? true,
  })
  const [touched, setTouched] = useState(false)
  const save = useAction((body: BranchBody) => (branch ? api.clinic.updateBranch(clinicId, branch.id, body) : api.clinic.createBranch(clinicId, body)))
  // Coordinates: both or neither, at most 6 decimals (API.md §4.1).
  const coord = (s: string, max: number) =>
    s.trim() === '' ? null : /^-?\d{1,3}(\.\d{1,6})?$/.test(s.trim()) && Math.abs(Number(s)) <= max ? null : t('branch.coordInvalid')
  const onlyOne = (v.latitude.trim() === '') !== (v.longitude.trim() === '')
  const req = (s: string, max: number) => (!s.trim() ? t('valid.required') : s.trim().length > max ? t('valid.maxLength', { max }) : null)
  const errors = {
    name: req(v.name, 200),
    district: req(v.district, 100),
    address: req(v.address, 500),
    phone: v.phone.trim() && !isValidUzPhone(v.phone) ? t('valid.phone') : null,
    timeZone: (branchTimeZones as readonly string[]).includes(v.timeZone) ? null : t('valid.required'),
    latitude: coord(v.latitude, 90) ?? (onlyOne ? t('branch.coordPair') : null),
    longitude: coord(v.longitude, 180) ?? (onlyOne ? t('branch.coordPair') : null),
  }
  const set = (k: keyof typeof v) => (e: { target: { value: string } }) => setV({ ...v, [k]: e.target.value })

  async function submit(e: FormEvent) {
    e.preventDefault()
    setTouched(true)
    if (Object.values(errors).some(Boolean)) return
    const r = await save.run({
      name: v.name.trim(),
      district: v.district.trim(),
      address: v.address.trim(),
      phone: v.phone.trim() ? normalizePhone(v.phone) : '',
      timeZone: v.timeZone,
      latitude: v.latitude.trim() ? Number(v.latitude) : null,
      longitude: v.longitude.trim() ? Number(v.longitude) : null,
      isActive: v.isActive,
    })
    if (r.ok) onDone()
  }
  const err = (k: keyof typeof errors) => (touched ? errors[k] : null)

  return (
    <Dialog title={branch ? t('branch.edit') : t('branch.add')} onClose={onClose}>
      <form className="stack" onSubmit={submit} noValidate>
        <Field label={t('common.name')} error={err('name')}>
          {(p) => <input {...p} maxLength={200} value={v.name} onChange={set('name')} />}
        </Field>
        <div className="grid-2">
          <Field label={t('home.district')} error={err('district')}>
            {(p) => <input {...p} maxLength={100} value={v.district} onChange={set('district')} />}
          </Field>
          <Field label={t('common.phone')} error={err('phone')} optional>
            {(p) => <input {...p} type="tel" value={v.phone} onChange={set('phone')} />}
          </Field>
        </div>
        <Field label={t('branch.address')} error={err('address')}>
          {(p) => <input {...p} maxLength={500} value={v.address} onChange={set('address')} />}
        </Field>
        <div className="grid-2">
          <Field label={t('branch.latitude')} error={err('latitude')} optional>
            {(p) => <input {...p} inputMode="decimal" value={v.latitude} onChange={set('latitude')} />}
          </Field>
          <Field label={t('branch.longitude')} error={err('longitude')} optional>
            {(p) => <input {...p} inputMode="decimal" value={v.longitude} onChange={set('longitude')} />}
          </Field>
        </div>
        <Field label={t('branch.timeZone')} hint={t('branch.timeZoneHint')} error={err('timeZone')}>
          {(p) => (
            <select {...p} value={v.timeZone} onChange={set('timeZone')}>
              {branchTimeZones.map((z) => (
                <option key={z} value={z}>
                  {t(`tz.${z}`)}
                </option>
              ))}
            </select>
          )}
        </Field>
        <Checkbox label={t('common.active')} checked={v.isActive} onChange={(isActive) => setV({ ...v, isActive })} />
        <FormError error={save.error} />
        <SubmitButton pending={save.pending}>{t('common.save')}</SubmitButton>
      </form>
    </Dialog>
  )
}

function HoursDialog({ branch, onDone, onClose }: { branch: BranchDto; onDone: () => void; onClose: () => void }) {
  const { t } = useI18n()
  const api = useApi()
  const { clinicId } = useClinic()
  const [value, setValue] = useState<Interval[]>(branch.hours.map((h) => ({ weekday: h.weekday, start: h.open, end: h.close })))
  const [touched, setTouched] = useState(false)
  const save = useAction(() => api.clinic.setBranchHours(clinicId, branch.id, value.map((i) => ({ weekday: i.weekday, open: i.start, close: i.end }))))

  async function submit(e: FormEvent) {
    e.preventDefault()
    setTouched(true)
    if (value.some((i) => intervalError(i)) || hasOverlap(value)) return
    const r = await save.run()
    if (r.ok) onDone()
  }

  return (
    <Dialog title={`${t('branch.hours')} — ${branch.name}`} onClose={onClose}>
      <form className="stack" onSubmit={submit} noValidate>
        <p className="muted small">{t('branch.hoursNote', { tz: branch.timeZone })}</p>
        <WeeklyHoursEditor value={value} onChange={setValue} legend={t('branch.hours')} showErrors={touched} />
        <FormError error={save.error} />
        <SubmitButton pending={save.pending}>{t('common.save')}</SubmitButton>
      </form>
    </Dialog>
  )
}
