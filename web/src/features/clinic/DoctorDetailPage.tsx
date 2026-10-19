import { CalendarOff, Pencil, Stethoscope, Trash2 } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { Link, useParams } from 'react-router'
import { useApi } from '../../api/context'
import type { DoctorDto, WorkingHoursEntry } from '../../api/types'
import { useSession } from '../../auth/session'
import { useAction } from '../../hooks/useAction'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { formatFullDateTime } from '../../lib/format'
import { addDays, localDate, splitLocalDateTime, zonedToUtc } from '../../lib/time'
import { Field, FormError, SubmitButton } from '../../ui/forms'
import { ConfirmDialog } from '../../ui/ReasonDialog'
import { EmptyState, ForbiddenState, QueryView } from '../../ui/states'
import { useToast } from '../../ui/toastContext'
import { Badge, PageHeader } from '../../ui/widgets'
import { useClinic } from './clinicContext'
import { DoctorDialog } from './DoctorsPage'
import { Perm } from './permissions'
import { WeeklyHoursEditor } from './WeeklyHoursEditor'
import { hasOverlap, intervalError, type Interval } from './hours'
import { useNow } from '../../hooks/useNow'

export function DoctorDetailPage() {
  const { doctorId = '' } = useParams()
  const { t } = useI18n()
  const api = useApi()
  const { me } = useSession()
  const { clinicId, can, branchName } = useClinic()
  const doctors = useQuery(`doctors:${clinicId}`, () => api.clinic.doctors(clinicId))
  const services = useQuery(`services:${clinicId}`, () => api.clinic.services(clinicId))
  const [editing, setEditing] = useState(false)
  const doctor = doctors.data?.find((d) => d.id === doctorId)
  useTitle(doctor?.displayName ?? t('clinicNav.doctors'))
  const manage = can(Perm.scheduleManage)

  return (
    <div className="stack">
      <nav className="breadcrumbs" aria-label={t('clinic.breadcrumbs')}>
        <Link to={`/clinic/${clinicId}/doctors`}>{t('clinicNav.doctors')}</Link>
      </nav>
      <QueryView query={doctors}>
        {() => {
          if (!doctor) return <EmptyState title={t('doc.notFound')} icon={Stethoscope} />
          const own = doctor.userId === me?.id && can(Perm.ownTimeOff)
          if (!manage && !own && !can(Perm.catalogManage)) return <ForbiddenState />
          return (
            <>
              <PageHeader
                icon={Stethoscope}
                title={doctor.displayName}
                subtitle={doctor.branchIds.map(branchName).join(', ')}
                actions={
                  manage &&
                  services.data && (
                    <button type="button" className="btn btn--ghost" onClick={() => setEditing(true)}>
                      <Pencil className="icon" aria-hidden="true" />
                      {t('common.edit')}
                    </button>
                  )
                }
              />
              {!doctor.isActive && <Badge>{t('common.inactive')}</Badge>}
              <WorkingHours doctor={doctor} editable={manage} />
              {(manage || own) && <TimeOff doctor={doctor} />}
              {editing && services.data && (
                <DoctorDialog
                  doctor={doctor}
                  services={services.data}
                  onDone={() => {
                    setEditing(false)
                    doctors.reload()
                  }}
                  onClose={() => setEditing(false)}
                />
              )}
            </>
          )
        }}
      </QueryView>
    </div>
  )
}

function WorkingHours({ doctor, editable }: { doctor: DoctorDto; editable: boolean }) {
  const { t } = useI18n()
  const api = useApi()
  const { clinicId, branches } = useClinic()
  const hours = useQuery(`working-hours:${doctor.id}`, () => api.clinic.workingHours(clinicId, doctor.id))
  const doctorBranches = branches.filter((b) => doctor.branchIds.includes(b.id))
  return (
    <section className="card stack" aria-labelledby="wh-title">
      <h2 id="wh-title">{t('doc.hours')}</h2>
      {doctorBranches.length === 0 ? (
        <p className="muted">{t('doc.noBranches')}</p>
      ) : (
        <QueryView query={hours}>{(entries) => <WorkingHoursForm doctor={doctor} initial={entries} editable={editable} onSaved={hours.reload} />}</QueryView>
      )}
    </section>
  )
}

function WorkingHoursForm({ doctor, initial, editable, onSaved }: { doctor: DoctorDto; initial: WorkingHoursEntry[]; editable: boolean; onSaved: () => void }) {
  const { t } = useI18n()
  const api = useApi()
  const { clinicId, branches, branchName } = useClinic()
  const doctorBranches = branches.filter((b) => doctor.branchIds.includes(b.id))
  const [branchId, setBranchId] = useState(doctorBranches[0]?.id ?? '')
  const [all, setAll] = useState<WorkingHoursEntry[]>(initial)
  const [touched, setTouched] = useState(false)
  const toast = useToast()
  const save = useAction((entries: WorkingHoursEntry[]) => api.clinic.setWorkingHours(clinicId, doctor.id, entries))
  const branch = doctorBranches.find((b) => b.id === branchId)
  const current: Interval[] = all.filter((e) => e.branchId === branchId).map((e) => ({ weekday: e.weekday, start: e.start, end: e.end }))
  const setCurrent = (list: Interval[]) => {
    setAll([...all.filter((e) => e.branchId !== branchId), ...list.map((i) => ({ branchId, ...i }))])
  }

  async function submit(e: FormEvent) {
    e.preventDefault()
    setTouched(true)
    const sent = all.filter((x) => doctorBranches.some((b) => b.id === x.branchId))
    if (sent.some((i) => intervalError(i)) || hasOverlap(sent)) return
    const r = await save.run(sent)
    if (r.ok) {
      toast({ title: t('common.saved') })
      onSaved()
    }
  }

  return (
    <form className="stack" onSubmit={submit} noValidate>
      {doctorBranches.length > 1 && (
        <div className="tabs" role="tablist" aria-label={t('common.branch')}>
          {doctorBranches.map((b) => (
            <button key={b.id} type="button" role="tab" aria-selected={b.id === branchId} className="tabs__tab" onClick={() => setBranchId(b.id)}>
              {b.name}
            </button>
          ))}
        </div>
      )}
      <p className="muted small">{t('doc.hoursNote', { branch: branchName(branchId), tz: branch?.timeZone ?? '' })}</p>
      {editable ? (
        <>
          <WeeklyHoursEditor value={current} onChange={setCurrent} legend={branchName(branchId)} showErrors={touched} />
          {touched && !hasOverlap(current) && hasOverlap(all.filter((x) => doctorBranches.some((b) => b.id === x.branchId))) && (
            <p className="field__error" role="alert">
              {t('doc.hoursOverlapBranches')}
            </p>
          )}
          <FormError error={save.error} />
          <SubmitButton pending={save.pending}>{t('common.save')}</SubmitButton>
        </>
      ) : (
        <ul className="list-plain">
          {current.length === 0 && <li className="muted">{t('doc.noHours')}</li>}
          {[...current]
            .sort((a, b) => a.weekday - b.weekday || a.start.localeCompare(b.start))
            .map((i, n) => (
              <li key={n}>
                {t(`weekdayLong.${i.weekday}`)}: {i.start}–{i.end}
              </li>
            ))}
        </ul>
      )}
    </form>
  )
}

function TimeOff({ doctor }: { doctor: DoctorDto }) {
  const now = useNow(60_000)
  const { t, locale } = useI18n()
  const api = useApi()
  const { clinicId, timeZoneFor } = useClinic()
  const tz = timeZoneFor(doctor.branchIds[0])
  const today = localDate(now, tz)
  const from = zonedToUtc(today, '00:00', tz)
  const to = zonedToUtc(addDays(today, 180), '00:00', tz)
  const list = useQuery(`timeoff:${doctor.id}`, () => api.clinic.timeOff(clinicId, doctor.id, from, to))
  const [start, setStart] = useState('')
  const [end, setEnd] = useState('')
  const [reason, setReason] = useState('')
  const [touched, setTouched] = useState(false)
  const [removing, setRemoving] = useState<string | null>(null)
  const toast = useToast()
  const add = useAction((body: { startsAt: string; endsAt: string; reason?: string }) => api.clinic.addTimeOff(clinicId, doctor.id, body))
  const s = splitLocalDateTime(start)
  const e2 = splitLocalDateTime(end)
  const startsAt = s ? zonedToUtc(s.date, s.time, tz) : null
  const endsAt = e2 ? zonedToUtc(e2.date, e2.time, tz) : null
  const error = !startsAt || !endsAt ? t('valid.required') : startsAt >= endsAt ? t('valid.timeRange') : null

  async function submit(e: FormEvent) {
    e.preventDefault()
    setTouched(true)
    if (error || !startsAt || !endsAt) return
    const r = await add.run({ startsAt, endsAt, ...(reason.trim() ? { reason: reason.trim() } : {}) })
    if (r.ok) {
      setStart('')
      setEnd('')
      setReason('')
      setTouched(false)
      toast({ title: t('common.saved') })
      list.reload()
    }
  }

  return (
    <section className="card stack" aria-labelledby="to-title">
      <h2 id="to-title">{t('doc.timeOff')}</h2>
      <QueryView query={list}>
        {(items) =>
          items.length === 0 ? (
            <p className="muted">{t('doc.noTimeOff')}</p>
          ) : (
            <ul className="list-plain stack">
              {items.map((x) => (
                <li key={x.id} className="row row--between">
                  <span className="row">
                    <CalendarOff className="icon muted" aria-hidden="true" />
                    {formatFullDateTime(x.startsAt, tz, locale)} — {formatFullDateTime(x.endsAt, tz, locale)}
                    {x.reason && <span className="muted"> · {x.reason}</span>}
                  </span>
                  <button type="button" className="btn btn--ghost btn--small" onClick={() => setRemoving(x.id)}>
                    <Trash2 className="icon" aria-hidden="true" />
                    {t('common.remove')}
                  </button>
                </li>
              ))}
            </ul>
          )
        }
      </QueryView>
      <form className="stack" onSubmit={submit} noValidate>
        <h3>{t('doc.addTimeOff')}</h3>
        <div className="grid-2">
          <Field label={t('common.from')} error={touched ? error : null}>
            {(p) => <input {...p} type="datetime-local" step={900} value={start} onChange={(ev) => setStart(ev.target.value)} />}
          </Field>
          <Field label={t('common.to')}>
            {(p) => <input {...p} type="datetime-local" step={900} min={start || undefined} value={end} onChange={(ev) => setEnd(ev.target.value)} />}
          </Field>
        </div>
        <Field label={t('common.reason')} hint={t('doc.timeOffReasonHint')} optional>
          {(p) => <input {...p} maxLength={200} value={reason} onChange={(ev) => setReason(ev.target.value)} />}
        </Field>
        <p className="muted small">{t('doc.timeOffNote', { tz })}</p>
        <FormError error={add.error} />
        <SubmitButton pending={add.pending}>{t('common.add')}</SubmitButton>
      </form>
      {removing && (
        <ConfirmDialog
          title={t('doc.removeTimeOff')}
          body={<p>{t('doc.removeTimeOffBody')}</p>}
          confirmLabel={t('common.remove')}
          danger
          onConfirm={() => api.clinic.deleteTimeOff(clinicId, doctor.id, removing)}
          onDone={() => {
            setRemoving(null)
            toast({ title: t('common.saved') })
            list.reload()
          }}
          onClose={() => setRemoving(null)}
        />
      )}
    </section>
  )
}
