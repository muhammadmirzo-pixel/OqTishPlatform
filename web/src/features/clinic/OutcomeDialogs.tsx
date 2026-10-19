import { useState, type FormEvent } from 'react'
import { useApi } from '../../api/context'
import type { AppointmentDto } from '../../api/types'
import { useAction } from '../../hooks/useAction'
import { useQuery } from '../../hooks/useQuery'
import { useI18n } from '../../i18n/context'
import { formatMoney } from '../../lib/format'
import { amountForInput, parseAmount, uzs } from '../../lib/money'
import { isValidDate, isValidTime, localDate, localTime, zonedToUtc } from '../../lib/time'
import { Dialog } from '../../ui/Dialog'
import { Select } from '../../ui/Select'
import { Field, FormError, SubmitButton } from '../../ui/forms'

/** Completed: charged amount pre-filled with the quoted price (OD-F8: any value ≥ 0). */
export function CompleteDialog({ clinicId, appointment, onDone, onClose }: { clinicId: string; appointment: AppointmentDto; onDone: () => void; onClose: () => void }) {
  const { t, locale } = useI18n()
  const api = useApi()
  const [amount, setAmount] = useState(amountForInput(appointment.quotedPrice.amount))
  const [touched, setTouched] = useState(false)
  const complete = useAction((value: string) => api.clinic.complete(clinicId, appointment.id, uzs(value)))
  const parsed = parseAmount(amount)
  const error = !amount.trim() ? t('valid.required') : parsed === null ? t('valid.amount') : null

  async function submit(e: FormEvent) {
    e.preventDefault()
    setTouched(true)
    if (error || !parsed) return
    const r = await complete.run(parsed)
    if (r.ok) onDone()
  }

  return (
    <Dialog title={t('act.complete')} onClose={onClose}>
      <form className="stack" onSubmit={submit} noValidate>
        <p className="muted">{t('act.completeIntro', { price: formatMoney(appointment.quotedPrice, locale) })}</p>
        <Field label={t('act.charged')} error={touched ? error : null}>
          {(p) => <input {...p} inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} />}
        </Field>
        <FormError error={complete.error} />
        <div className="row">
          <SubmitButton pending={complete.pending}>{t('act.complete')}</SubmitButton>
          <button type="button" className="btn btn--ghost" onClick={onClose}>
            {t('common.cancel')}
          </button>
        </div>
      </form>
    </Dialog>
  )
}

/** Clinic-initiated move of a Confirmed appointment (same service) to a new time and doctor (OD-05). */
export function RescheduleDialog({ clinicId, appointment, onDone, onClose }: { clinicId: string; appointment: AppointmentDto; onDone: () => void; onClose: () => void }) {
  const { t } = useI18n()
  const api = useApi()
  const tz = appointment.timeZone
  const [date, setDate] = useState(localDate(appointment.startsAt, tz))
  const [time, setTime] = useState(localTime(appointment.startsAt, tz))
  const [doctorId, setDoctorId] = useState(appointment.doctor?.id ?? '')
  const [touched, setTouched] = useState(false)
  const doctors = useQuery(`doctors:${clinicId}`, () => api.clinic.doctors(clinicId))
  const eligible = (doctors.data ?? []).filter((d) => d.isActive && d.branchIds.includes(appointment.branchId) && d.serviceIds.includes(appointment.serviceId))
  const move = useAction((body: { startsAt: string; doctorId: string }) => api.clinic.reschedule(clinicId, appointment.id, body))
  const errors = {
    date: !isValidDate(date) ? t('valid.required') : null,
    time: !isValidTime(time) ? t('valid.time') : null,
    doctor: !doctorId ? t('req.chooseDoctor') : null,
  }

  async function submit(e: FormEvent) {
    e.preventDefault()
    setTouched(true)
    if (errors.date || errors.time || errors.doctor) return
    const r = await move.run({ startsAt: zonedToUtc(date, time, tz), doctorId })
    if (r.ok) onDone()
  }

  return (
    <Dialog title={t('act.reschedule')} onClose={onClose}>
      <form className="stack" onSubmit={submit} noValidate>
        <p className="muted small">{t('act.rescheduleIntro', { tz })}</p>
        <div className="grid-2">
          <Field label={t('common.date')} error={touched ? errors.date : null}>
            {(p) => <input {...p} type="date" value={date} onChange={(e) => setDate(e.target.value)} />}
          </Field>
          <Field label={t('common.time')} error={touched ? errors.time : null}>
            {(p) => <input {...p} type="time" step={900} value={time} onChange={(e) => setTime(e.target.value)} />}
          </Field>
        </div>
        <Field label={t('common.doctor')} error={touched ? errors.doctor : null}>
          {(p) => (
            <Select {...p} value={doctorId} onChange={setDoctorId} options={[{ value: '', label: t('req.chooseDoctor') }, ...eligible.map((d) => ({ value: d.id, label: d.displayName }))]} />
          )}
        </Field>
        <FormError error={move.error ?? doctors.error} />
        <div className="row">
          <SubmitButton pending={move.pending}>{t('act.reschedule')}</SubmitButton>
          <button type="button" className="btn btn--ghost" onClick={onClose}>
            {t('common.cancel')}
          </button>
        </div>
      </form>
    </Dialog>
  )
}
