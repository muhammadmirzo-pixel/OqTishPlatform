import { UserPlus } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router'
import { useApi } from '../../api/context'
import { useIdempotencyKeeper } from '../../api/idempotency'
import type { WalkInBody } from '../../api/types'
import { useAction } from '../../hooks/useAction'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { formatMoney } from '../../lib/format'
import { isValidUzPhone, normalizePhone } from '../../lib/phone'
import { isValidDate, isValidTime, localDate, zonedToUtc } from '../../lib/time'
import { Field, FormError, SubmitButton } from '../../ui/forms'
import { Select } from '../../ui/Select'
import { DatePicker, TimeSelect } from '../../ui/DatePicker'
import { PageHeader } from '../../ui/widgets'
import { useBranchParam } from './branchParam'
import { useClinic } from './clinicContext'
import { useNow } from '../../hooks/useNow'

/** Walk-in / phone booking by staff: created directly as Confirmed with a chosen doctor (OD-08). */
export function WalkInPage() {
  const now = useNow(60_000)
  const { t, locale } = useI18n()
  useTitle(t('clinicNav.walkIn'))
  const api = useApi()
  const navigate = useNavigate()
  const { clinicId, branches } = useClinic()
  const [branchParam] = useBranchParam()
  const keeper = useIdempotencyKeeper()
  const [branchId, setBranchId] = useState(branchParam ?? (branches.length === 1 ? branches[0]!.id : ''))
  const [serviceId, setServiceId] = useState('')
  const branch = branches.find((b) => b.id === branchId)
  const tz = branch?.timeZone ?? 'Asia/Tashkent'
  const [date, setDate] = useState(localDate(now, tz))
  const [time, setTime] = useState('')
  const [doctorId, setDoctorId] = useState('')
  const [name, setName] = useState('')
  const [phone, setPhone] = useState('')
  const [touched, setTouched] = useState(false)

  const services = useQuery(`services:${clinicId}`, () => api.clinic.services(clinicId))
  const doctors = useQuery(`doctors:${clinicId}`, () => api.clinic.doctors(clinicId))
  const offered = (services.data ?? []).filter((s) => s.isActive && s.offers.some((o) => o.branchId === branchId && o.isActive))
  const offer = offered.find((s) => s.id === serviceId)?.offers.find((o) => o.branchId === branchId)
  const eligible = (doctors.data ?? []).filter((d) => d.isActive && d.branchIds.includes(branchId) && d.serviceIds.includes(serviceId))
  const slots = useQuery(branchId && serviceId && isValidDate(date) ? `walkin-slots:${branchId}:${serviceId}:${date}` : null, () => api.public.availability(branchId, serviceId, date))
  const create = useAction((body: WalkInBody) => api.clinic.walkIn(clinicId, body, keeper.keyFor(body)))

  const errors = {
    branch: !branchId ? t('valid.required') : null,
    service: !serviceId ? t('valid.required') : null,
    date: !isValidDate(date) ? t('valid.required') : null,
    time: !isValidTime(time) ? t('valid.time') : null,
    doctor: !doctorId ? t('req.chooseDoctor') : null,
    name: !name.trim() ? t('valid.required') : name.length > 100 ? t('valid.maxLength', { max: 100 }) : null,
    phone: !isValidUzPhone(phone) ? t('valid.phone') : null,
  }

  async function submit(e: FormEvent) {
    e.preventDefault()
    setTouched(true)
    if (Object.values(errors).some(Boolean)) return
    const body: WalkInBody = { branchId, serviceId, startsAt: zonedToUtc(date, time, tz), doctorId, contactName: name.trim(), contactPhone: normalizePhone(phone) }
    const r = await create.run(body)
    if (r.ok) {
      keeper.reset()
      navigate(`/clinic/${clinicId}/appointments/${r.value.id}`)
    } else keeper.afterError(r.error)
  }

  const err = (k: keyof typeof errors) => (touched ? errors[k] : null)

  return (
    <div className="stack">
      <PageHeader icon={UserPlus} title={t('clinicNav.walkIn')} subtitle={t('walkin.intro')} />
      <form className="card stack" onSubmit={submit} noValidate>
        <div className="grid-2">
          <Field label={t('common.branch')} error={err('branch')}>
            {(p) => (
              <Select
                {...p}
                value={branchId}
                onChange={(v) => {
                  setBranchId(v)
                  setServiceId('')
                  setDoctorId('')
                }}
                options={[{ value: '', label: t('walkin.chooseBranch') }, ...branches.map((b) => ({ value: b.id, label: b.name }))]}
              />
            )}
          </Field>
          <Field label={t('common.service')} hint={offer ? `${formatMoney(offer.price, locale)} · ${t('common.minutes', { minutes: offer.durationMinutes })}` : undefined} error={err('service')}>
            {(p) => (
              <Select
                {...p}
                value={serviceId}
                disabled={!branchId}
                onChange={(v) => {
                  setServiceId(v)
                  setDoctorId('')
                }}
                options={[{ value: '', label: t('walkin.chooseService') }, ...offered.map((s) => ({ value: s.id, label: s.name }))]}
              />
            )}
          </Field>
          <Field label={t('common.date')} error={err('date')}>
            {(p) => <DatePicker {...p} value={date} onChange={setDate} />}
          </Field>
          <Field label={t('common.time')} hint={t('walkin.timeHint', { tz })} error={err('time')}>
            {(p) => <TimeSelect {...p} value={time} onChange={setTime} />}
          </Field>
        </div>
        {slots.data && slots.data.slots.length > 0 && (
          <div className="stack">
            <p className="small muted">{t('walkin.suggested')}</p>
            <div className="slots">
              {slots.data.slots.map((s) => (
                <button key={s.startsAt} type="button" className={s.localTime === time ? 'slot slot--selected' : 'slot'} aria-pressed={s.localTime === time} onClick={() => setTime(s.localTime)}>
                  {s.localTime}
                </button>
              ))}
            </div>
          </div>
        )}
        <Field label={t('common.doctor')} hint={serviceId && eligible.length === 0 ? t('walkin.noDoctors') : undefined} error={err('doctor')}>
          {(p) => (
            <Select
              {...p}
              value={doctorId}
              disabled={!serviceId}
              onChange={setDoctorId}
              options={[{ value: '', label: t('req.chooseDoctor') }, ...eligible.map((d) => ({ value: d.id, label: d.displayName }))]}
            />
          )}
        </Field>
        <div className="grid-2">
          <Field label={t('book.name')} error={err('name')}>
            {(p) => <input {...p} maxLength={100} autoComplete="off" value={name} onChange={(e) => setName(e.target.value)} />}
          </Field>
          <Field label={t('book.phone')} error={err('phone')}>
            {(p) => <input {...p} type="tel" inputMode="tel" autoComplete="off" placeholder="+998 90 123 45 67" value={phone} onChange={(e) => setPhone(e.target.value)} />}
          </Field>
        </div>
        <p className="muted small">{t('walkin.consent')}</p>
        <FormError error={create.error ?? services.error ?? doctors.error} />
        <SubmitButton pending={create.pending}>{t('walkin.submit')}</SubmitButton>
      </form>
    </div>
  )
}
