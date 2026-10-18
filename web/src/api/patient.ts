import { query, seg, type ApiClient } from './client'
import type { AppointmentDto, AppointmentStatus, CreateAppointmentBody, Page, ReportBody, ReviewBody } from './types'

/** docs/API.md §3 — patient booking and own appointments. */
export function patientApi(c: ApiClient) {
  return {
    book: (body: CreateAppointmentBody, idempotencyKey: string) => c.send<AppointmentDto>('POST', '/appointments', body, { idempotencyKey }),
    list: (params: { status?: AppointmentStatus; page?: number }) => c.get<Page<AppointmentDto>>(`/me/appointments${query(params)}`),
    get: (id: string) => c.get<AppointmentDto>(`/me/appointments/${seg(id)}`),
    cancel: (id: string) => c.send<AppointmentDto | undefined>('POST', `/me/appointments/${seg(id)}/cancel`),
    review: (id: string, body: ReviewBody) => c.send<void>('POST', `/me/appointments/${seg(id)}/review`, body),
    report: (body: ReportBody) => c.send<void>('POST', '/reports', body),
  }
}
