import { query, seg, type ApiClient } from './client'
import type {
  AuditEntryDto,
  Balance,
  DailyReportRow,
  MonthlyReport,
  Page,
  PaymentDto,
  RecordPaymentBody,
  RefundBody,
  SettlementDto,
} from './types'

/** docs/API.md §4 — payments, reports, settlements and the clinic audit log. */
export function financeApi(c: ApiClient) {
  const base = (clinicId: string) => `/clinics/${seg(clinicId)}`
  return {
    payments: (clinicId: string, params: { branchId?: string; from?: string; to?: string; appointmentId?: string; page?: number; pageSize?: number }) =>
      c.get<Page<PaymentDto>>(`${base(clinicId)}/payments${query(params)}`),
    balance: (clinicId: string, appointmentId: string) => c.get<Balance>(`${base(clinicId)}/appointments/${seg(appointmentId)}/balance`),
    recordPayment: (clinicId: string, appointmentId: string, body: RecordPaymentBody, idempotencyKey: string) =>
      c.send<PaymentDto>('POST', `${base(clinicId)}/appointments/${seg(appointmentId)}/payments`, body, { idempotencyKey }),
    failPayment: (clinicId: string, paymentId: string, reason: string) =>
      c.send<void>('POST', `${base(clinicId)}/payments/${seg(paymentId)}/fail`, { reason }),
    refund: (clinicId: string, paymentId: string, body: RefundBody, idempotencyKey: string) =>
      c.send<PaymentDto>('POST', `${base(clinicId)}/payments/${seg(paymentId)}/refund`, body, { idempotencyKey }),
    daily: (clinicId: string, params: { branchId?: string; from: string; to: string }) =>
      c.get<DailyReportRow[]>(`${base(clinicId)}/reports/daily${query(params)}`),
    monthly: (clinicId: string, params: { year: number; month: number; branchId?: string }) =>
      c.get<MonthlyReport>(`${base(clinicId)}/reports/monthly${query(params)}`),
    settlements: (clinicId: string) => c.get<SettlementDto[]>(`${base(clinicId)}/settlements`),
    audit: (clinicId: string, page = 1) => c.get<Page<AuditEntryDto>>(`${base(clinicId)}/audit${query({ page })}`),
  }
}
