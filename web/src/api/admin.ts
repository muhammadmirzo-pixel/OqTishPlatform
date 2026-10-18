import { query, seg, type ApiClient } from './client'
import type {
  AbuseReportDto,
  AdminClinicDto,
  AdminDashboardDto,
  AdminReviewDto,
  AdminUserDto,
  AppointmentDto,
  AuditEntryDto,
  ClinicAppointmentFilters,
  ClinicVerification,
  CommissionRuleBody,
  CommissionRuleDto,
  NotificationDto,
  NotificationStatus,
  Page,
  ReportStatus,
  ReviewModerationStatus,
  SettlementDto,
  SettlementStatus,
} from './types'

/**
 * The break-glass reason travels in a header, and header values must be ISO-8859-1. Reasons are
 * typed in Uzbek/Russian, so the value is percent-encoded (UTF-8); the server decodes it.
 */
export function encodeAccessReason(reason: string): string {
  return encodeURIComponent(reason.trim())
}

/** docs/API.md §5 — platform admin. */
export function adminApi(c: ApiClient) {
  return {
    dashboard: () => c.get<AdminDashboardDto>('/admin/dashboard'),
    clinics: (params: { status?: ClinicVerification; q?: string; page?: number; pageSize?: number }) =>
      c.get<Page<AdminClinicDto>>(`/admin/clinics${query(params)}`),
    approveClinic: (id: string) => c.send<AdminClinicDto>('POST', `/admin/clinics/${seg(id)}/approve`),
    rejectClinic: (id: string, reason: string) => c.send<AdminClinicDto>('POST', `/admin/clinics/${seg(id)}/reject`, { reason }),
    suspendClinic: (id: string, reason: string) => c.send<AdminClinicDto>('POST', `/admin/clinics/${seg(id)}/suspend`, { reason }),
    reinstateClinic: (id: string) => c.send<AdminClinicDto>('POST', `/admin/clinics/${seg(id)}/reinstate`),

    users: (params: { q?: string; page?: number; pageSize?: number }) => c.get<Page<AdminUserDto>>(`/admin/users${query(params)}`),
    suspendUser: (id: string, reason: string) => c.send<void>('POST', `/admin/users/${seg(id)}/suspend`, { reason }),
    reinstateUser: (id: string) => c.send<void>('POST', `/admin/users/${seg(id)}/reinstate`),

    reports: (params: { status?: ReportStatus; page?: number }) => c.get<Page<AbuseReportDto>>(`/admin/reports${query(params)}`),
    resolveReport: (id: string, resolution: string) => c.send<void>('POST', `/admin/reports/${seg(id)}/resolve`, { resolution }),

    reviews: (params: { status?: ReviewModerationStatus; page?: number }) => c.get<Page<AdminReviewDto>>(`/admin/reviews${query(params)}`),
    publishReview: (id: string) => c.send<void>('POST', `/admin/reviews/${seg(id)}/publish`),
    hideReview: (id: string, reason: string) => c.send<void>('POST', `/admin/reviews/${seg(id)}/hide`, { reason }),

    commissionRules: (clinicId: string) => c.get<CommissionRuleDto[]>(`/admin/clinics/${seg(clinicId)}/commission-rules`),
    createCommissionRule: (clinicId: string, body: CommissionRuleBody) =>
      c.send<CommissionRuleDto>('POST', `/admin/clinics/${seg(clinicId)}/commission-rules`, body),
    endCommissionRule: (clinicId: string, ruleId: string, effectiveTo: string) =>
      c.send<void>('POST', `/admin/clinics/${seg(clinicId)}/commission-rules/${seg(ruleId)}/end`, { effectiveTo }),

    settlements: (params: { clinicId?: string; status?: SettlementStatus }) => c.get<SettlementDto[]>(`/admin/settlements${query(params)}`),
    createSettlement: (body: { clinicId: string; year: number; month: number }) => c.send<SettlementDto>('POST', '/admin/settlements', body),
    finalizeSettlement: (id: string) => c.send<void>('POST', `/admin/settlements/${seg(id)}/finalize`),
    markSettlementPaid: (id: string, body: { paidAt: string; reference: string }) =>
      c.send<void>('POST', `/admin/settlements/${seg(id)}/mark-paid`, body),
    voidSettlement: (id: string, reason: string) => c.send<void>('POST', `/admin/settlements/${seg(id)}/void`, { reason }),

    tenantAppointments: (clinicId: string, filters: ClinicAppointmentFilters, reason: string) =>
      c.get<Page<AppointmentDto>>(`/admin/clinics/${seg(clinicId)}/appointments${query(filters)}`, {
        headers: { 'X-Access-Reason': encodeAccessReason(reason) },
      }),

    audit: (params: { clinicId?: string; action?: string; page?: number; pageSize?: number }) => c.get<Page<AuditEntryDto>>(`/admin/audit${query(params)}`),
    notifications: (params: { status?: NotificationStatus; page?: number }) =>
      c.get<Page<NotificationDto>>(`/admin/notifications${query(params)}`),
    retryNotification: (id: string) => c.send<void>('POST', `/admin/notifications/${seg(id)}/retry`),
  }
}
