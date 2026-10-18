import type { AppointmentDto } from '../../api/types'
import type { Locale, MessageKey } from '../../i18n/messages'
import { formatDate, formatTime } from '../../lib/format'
import { localDate } from '../../lib/time'

/** "20 oktabr 2026, 10:00–10:30" in the branch time zone. */
export function appointmentWhen(a: Pick<AppointmentDto, 'startsAt' | 'endsAt' | 'timeZone'>, locale: Locale): string {
  return `${formatDate(localDate(a.startsAt, a.timeZone), locale)}, ${formatTime(a.startsAt, a.timeZone, locale)}–${formatTime(a.endsAt, a.timeZone, locale)}`
}

export function durationMinutes(a: Pick<AppointmentDto, 'startsAt' | 'endsAt'>): number {
  return Math.round((new Date(a.endsAt).getTime() - new Date(a.startsAt).getTime()) / 60000)
}

export const rejectReasons = ['NoDoctorAvailable', 'TimeUnavailable', 'ServiceUnavailable', 'PatientUnreachable', 'Other'] as const
export const cancelReasons = ['DoctorUnavailable', 'ClinicClosed', 'PatientRequest', 'ClinicEmergency', 'Other'] as const
type Reason = (typeof rejectReasons)[number] | (typeof cancelReasons)[number]
const knownReasons: readonly string[] = [...rejectReasons, ...cancelReasons]

export function reasonLabel(t: (key: MessageKey, params?: Record<string, string | number>) => string, code: string): string {
  return knownReasons.includes(code) ? t(`reason.${code as Reason}`) : code
}
