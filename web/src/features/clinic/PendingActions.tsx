import { useState } from 'react'
import { isApiError } from '../../api/client'
import { useApi } from '../../api/context'
import type { AppointmentDto } from '../../api/types'
import { useAction } from '../../hooks/useAction'
import { useQuery } from '../../hooks/useQuery'
import { useI18n } from '../../i18n/context'
import { FormError } from '../../ui/forms'
import { Select } from '../../ui/Select'
import { rejectReasons } from '../appointments/format'
import { ReasonCodeDialog } from './ReasonCodeDialog'

export type TriageOutcome = { kind: 'confirmed' | 'rejected' } | { kind: 'stale'; error: unknown }

/** Errors meaning "the request is no longer what you see": refresh instead of retrying. */
const staleCodes = ['booking.expired', 'booking.not_pending', 'booking.invalid_transition']

/**
 * Confirm (with an eligible doctor) or reject a Pending request. The server rechecks deadline,
 * eligibility and overlap atomically; stale answers are reported to the caller to refresh.
 */
export function PendingActions({ clinicId, appointment, onOutcome }: { clinicId: string; appointment: AppointmentDto; onOutcome: (outcome: TriageOutcome) => void }) {
  const { t } = useI18n()
  const api = useApi()
  const doctors = useQuery(`eligible:${appointment.id}`, () => api.clinic.eligibleDoctors(clinicId, appointment.id))
  const [chosen, setDoctorId] = useState('')
  const [touched, setTouched] = useState(false)
  const [rejecting, setRejecting] = useState(false)
  const confirm = useAction((id: string) => api.clinic.confirm(clinicId, appointment.id, id))

  // A single eligible doctor is pre-selected; the choice is still shown and can be confirmed.
  const doctorId = chosen || (doctors.data?.length === 1 ? doctors.data[0]!.id : '')

  const selectId = `doctor-${appointment.id}`
  const noDoctors = doctors.data !== undefined && doctors.data.length === 0

  async function doConfirm() {
    setTouched(true)
    if (!doctorId) return
    const r = await confirm.run(doctorId)
    if (r.ok) {
      onOutcome({ kind: 'confirmed' })
      return
    }
    if (staleCodes.some((c) => isApiError(r.error, c))) {
      // Reported once by the caller (which refreshes the list), not repeated on the card.
      confirm.clearError()
      onOutcome({ kind: 'stale', error: r.error })
    } else if (isApiError(r.error, 'booking.doctor_busy') || isApiError(r.error, 'booking.doctor_not_eligible')) {
      setDoctorId('')
      doctors.reload()
    }
  }

  return (
    <div className="triage">
      <div className="triage__doctor field">
        <label id={`${selectId}-label`} htmlFor={selectId}>
          {t('req.assignDoctor')}
        </label>
        <Select
          id={selectId}
          value={doctorId}
          disabled={!doctors.data || noDoctors}
          aria-invalid={touched && !doctorId ? true : undefined}
          onChange={(v) => {
            setDoctorId(v)
            confirm.clearError()
          }}
          options={[
            { value: '', label: doctors.loading && !doctors.data ? t('common.loading') : t('req.chooseDoctor') },
            ...(doctors.data ?? []).map((d) => ({
              value: d.id,
              label: `${d.name}${d.provisional ? ` · ${t('req.provisionalMark')}` : ''}${d.withinWorkingHours ? '' : ` · ${t('req.outsideHoursMark')}`}`,
            })),
          ]}
        />
        {doctors.data?.find((d) => d.id === doctorId)?.withinWorkingHours === false && <p className="field__hint">{t('req.outsideHours')}</p>}
        {noDoctors && <p className="field__hint">{t('req.noEligible')}</p>}
        {touched && !doctorId && !noDoctors && <p className="field__error">{t('req.chooseDoctor')}</p>}
        {doctors.error !== undefined && <FormError error={doctors.error} />}
      </div>
      <div className="row">
        <button type="button" className="btn" disabled={confirm.pending || noDoctors} onClick={() => void doConfirm()}>
          {confirm.pending ? t('common.saving') : t('req.confirm')}
        </button>
        <button type="button" className="btn btn--ghost" onClick={() => setRejecting(true)}>
          {t('req.reject')}
        </button>
      </div>
      <FormError error={confirm.error} />
      {rejecting && (
        <ReasonCodeDialog
          title={t('req.rejectTitle')}
          codes={rejectReasons}
          submitLabel={t('req.reject')}
          onSubmit={(code) => api.clinic.reject(clinicId, appointment.id, code)}
          onDone={() => {
            setRejecting(false)
            onOutcome({ kind: 'rejected' })
          }}
          onError={(error) => {
            if (staleCodes.some((c) => isApiError(error, c))) {
              setRejecting(false)
              onOutcome({ kind: 'stale', error })
            }
          }}
          onClose={() => setRejecting(false)}
        />
      )}
    </div>
  )
}
