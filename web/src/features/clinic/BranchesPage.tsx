import { useState, type FormEvent } from 'react'
import { useApi } from '../../api/context'
import type { BranchBody, BranchDto } from '../../api/types'
import { useAction } from '../../hooks/useAction'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { isValidUzPhone, normalizePhone } from '../../lib/phone'
import { DEFAULT_TIME_ZONE } from '../../lib/time'
import { Dialog } from '../../ui/Dialog'
import { Checkbox, Field, FormError, Notice, SubmitButton } from '../../ui/forms'
import { EmptyState } from '../../ui/states'
import { Badge, PageHeader } from '../../ui/widgets'
import { useClinic } from './clinicContext'
import { Perm } from './permissions'
import { WeeklyHoursEditor } from './WeeklyHoursEditor'
import { hasOverlap, intervalError, type Interval } from './hours'

export function BranchesPage() {
  const { t } = useI18n()
  useTitle(t('clinicNav.branches'))
  const { branches, can, reloadBranches } = useClinic()
  const [editing, setEditing] = useState<BranchDto | 'new' | null>(null)
  const [hoursFor, setHoursFor] = useState<BranchDto | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const manage = can(Perm.branchManage)

  return (
    <div className="stack">
      <PageHeader
        title={t('clinicNav.branches')}
        actions={
          manage && (
            <button type="button" className="btn" onClick={() => setEditing('new')}>
              {t('branch.add')}
            </button>
          )
        }
      />
      <div aria-live="polite">{notice && <Notice>{notice}</Notice>}</div>
      {branches.length === 0 ? (
        <EmptyState title={t('branch.none')} />
      ) : (
        <ul className="list-plain stack">
          {branches.map((b) => (
            <li key={b.id} className="card row row--between">
              <div>
                <strong>{b.name}</strong> {!b.isActive && <Badge>{t('common.inactive')}</Badge>}
                <div className="muted small">
                  {b.district}, {b.address} · {b.phone} · {b.timeZone}
                </div>
                <div className="small">
                  {b.hours.length === 0
                    ? t('branch.noHours')
                    : [...b.hours]
                        .sort((x, y) => x.weekday - y.weekday)
                        .map((h) => `${t(`weekday.${h.weekday}`)} ${h.open}–${h.close}`)
                        .join(' · ')}
                </div>
              </div>
              {manage && (
                <div className="row">
                  <button type="button" className="btn btn--ghost btn--small" onClick={() => setEditing(b)}>
                    {t('common.edit')}
                  </button>
                  <button type="button" className="btn btn--ghost btn--small" onClick={() => setHoursFor(b)}>
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
            setNotice(t('common.saved'))
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
            setNotice(t('common.saved'))
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
    timeZone: branch?.timeZone ?? DEFAULT_TIME_ZONE,
    latitude: branch?.latitude?.toString() ?? '',
    longitude: branch?.longitude?.toString() ?? '',
    isActive: branch?.isActive ?? true,
  })
  const [touched, setTouched] = useState(false)
  const save = useAction((body: BranchBody) => (branch ? api.clinic.updateBranch(clinicId, branch.id, body) : api.clinic.createBranch(clinicId, body)))
  const coord = (s: string, max: number) => (s.trim() === '' ? null : Number.isFinite(Number(s)) && Math.abs(Number(s)) <= max ? null : t('branch.coordInvalid'))
  const req = (s: string) => (!s.trim() ? t('valid.required') : null)
  const errors = {
    name: req(v.name),
    district: req(v.district),
    address: req(v.address),
    phone: !isValidUzPhone(v.phone) ? t('valid.phone') : null,
    timeZone: req(v.timeZone),
    latitude: coord(v.latitude, 90),
    longitude: coord(v.longitude, 180),
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
      phone: normalizePhone(v.phone),
      timeZone: v.timeZone.trim(),
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
          {(p) => <input {...p} maxLength={120} value={v.name} onChange={set('name')} />}
        </Field>
        <div className="grid-2">
          <Field label={t('home.district')} error={err('district')}>
            {(p) => <input {...p} maxLength={100} value={v.district} onChange={set('district')} />}
          </Field>
          <Field label={t('common.phone')} error={err('phone')}>
            {(p) => <input {...p} type="tel" value={v.phone} onChange={set('phone')} />}
          </Field>
        </div>
        <Field label={t('branch.address')} error={err('address')}>
          {(p) => <input {...p} maxLength={300} value={v.address} onChange={set('address')} />}
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
          {(p) => <input {...p} value={v.timeZone} onChange={set('timeZone')} />}
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
