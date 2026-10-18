import type { ClinicDetail } from '../../api/types'

export const bookingSteps = ['branch', 'service', 'time', 'contact', 'summary'] as const
export type BookingStep = (typeof bookingSteps)[number]

export const HORIZON_DAYS = 30

export interface BookingState {
  step: BookingStep
  branchId?: string
  serviceId?: string
  date?: string
  startsAt?: string
  contactName?: string
  contactPhone?: string
}

/** Clamp the requested step to the first one whose prerequisites are missing. */
export function effectiveStep(state: BookingState, clinic: ClinicDetail | undefined): BookingStep {
  const branch = clinic?.branches.find((b) => b.id === state.branchId)
  const service = branch?.services.find((s) => s.serviceId === state.serviceId)
  const want = bookingSteps.indexOf(state.step)
  if (!branch) return 'branch'
  if (want >= 1 && !service) return 'service'
  if (want >= 3 && !state.startsAt) return 'time'
  if (want >= 4 && (!state.contactName || !state.contactPhone)) return 'contact'
  return state.step
}

export interface SubmitControlProps {
  label: string
  pending: boolean
  onSubmit: () => void
}
