import { useCallback } from 'react'
import { useLocation, useNavigate, useParams, useSearchParams } from 'react-router'
import { loginPath } from '../../auth/session'
import { useSession } from '../../auth/session'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { BookingWizard, type BookingState, type BookingStep } from './BookingWizard'
import { bookingSteps } from './bookingState'

/**
 * Web booking page. Wizard selections live in the URL query, so a patient who has to sign in
 * at the contact step returns to exactly the same branch/service/time. Contact details are not
 * put in the URL (personal data); they stay in history state only.
 */
export function BookingPage() {
  const { clinicId = '' } = useParams()
  const { t } = useI18n()
  useTitle(t('book.title'))
  const [params, setParams] = useSearchParams()
  const navigate = useNavigate()
  const { me } = useSession()

  const stepParam = params.get('step') as BookingStep | null
  const location = useLocation()
  const contact = location.state as { contactName?: string; contactPhone?: string } | null
  const state: BookingState = {
    step: stepParam && bookingSteps.includes(stepParam) ? stepParam : 'branch',
    branchId: params.get('branch') ?? undefined,
    serviceId: params.get('service') ?? undefined,
    date: params.get('date') ?? undefined,
    startsAt: params.get('start') ?? undefined,
    contactName: contact?.contactName,
    contactPhone: contact?.contactPhone,
  }

  const onChange = useCallback(
    (next: BookingState) => {
      const search = new URLSearchParams()
      search.set('step', next.step)
      if (next.branchId) search.set('branch', next.branchId)
      if (next.serviceId) search.set('service', next.serviceId)
      if (next.date) search.set('date', next.date)
      if (next.startsAt) search.set('start', next.startsAt)
      setParams(search, { state: { contactName: next.contactName, contactPhone: next.contactPhone } })
    },
    [setParams],
  )

  const here = `/book/${clinicId}?${params.toString()}`
  return (
    <div className="page booking-page">
      <BookingWizard
        clinicId={clinicId}
        state={state}
        onChange={onChange}
        loginHref={me ? null : loginPath(here)}
        onBooked={(appointment) => navigate(`/me/appointments/${appointment.id}`, { replace: true, state: { justBooked: true } })}
      />
    </div>
  )
}
