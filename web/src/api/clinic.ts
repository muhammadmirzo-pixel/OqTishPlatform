import { imageForm, query, seg, type ApiClient } from './client'
import type {
  AppointmentDto,
  BranchBody,
  BranchDto,
  BranchOfferBody,
  CalendarDto,
  ClinicAppointmentFilters,
  ClinicProfileDto,
  DashboardDto,
  DoctorBody,
  DoctorDto,
  EligibleDoctor,
  InvitationBody,
  InvitationDto,
  Money,
  OpeningHours,
  Page,
  RegisterClinicBody,
  ServiceBody,
  ServiceDto,
  StaffBody,
  StaffMemberDto,
  TimeOffDto,
  WalkInBody,
  WorkingHoursEntry,
} from './types'

/** docs/API.md §4 — clinic console: profile, branches, catalogue, staff, doctors, schedule. */
export function clinicApi(c: ApiClient) {
  const base = (clinicId: string) => `/clinics/${seg(clinicId)}`
  return {
    register: (body: RegisterClinicBody) => c.send<ClinicProfileDto>('POST', '/clinics', body),
    profile: (clinicId: string) => c.get<ClinicProfileDto>(base(clinicId)),
    updateProfile: (clinicId: string, body: RegisterClinicBody) => c.send<ClinicProfileDto>('PUT', base(clinicId), body),
    /** multipart field `file`: JPEG/PNG/WebP up to 2 MB. */
    uploadLogo: (clinicId: string, file: File) => c.send<{ logoId: string }>('PUT', `${base(clinicId)}/logo`, imageForm(file)),
    deleteLogo: (clinicId: string) => c.send<void>('DELETE', `${base(clinicId)}/logo`),
    dashboard: (clinicId: string, branchId?: string) => c.get<DashboardDto>(`${base(clinicId)}/dashboard${query({ branchId })}`),

    branches: (clinicId: string) => c.get<BranchDto[]>(`${base(clinicId)}/branches`),
    createBranch: (clinicId: string, body: BranchBody) => c.send<BranchDto>('POST', `${base(clinicId)}/branches`, body),
    updateBranch: (clinicId: string, branchId: string, body: BranchBody) =>
      c.send<BranchDto>('PUT', `${base(clinicId)}/branches/${seg(branchId)}`, body),
    /** → 204; replaces all opening hours of the branch. */
    setBranchHours: (clinicId: string, branchId: string, hours: OpeningHours[]) =>
      c.send<void>('PUT', `${base(clinicId)}/branches/${seg(branchId)}/hours`, hours),

    services: (clinicId: string) => c.get<ServiceDto[]>(`${base(clinicId)}/services`),
    createService: (clinicId: string, body: ServiceBody) => c.send<ServiceDto>('POST', `${base(clinicId)}/services`, body),
    updateService: (clinicId: string, serviceId: string, body: ServiceBody) =>
      c.send<ServiceDto>('PUT', `${base(clinicId)}/services/${seg(serviceId)}`, body),
    /** → 204 (upsert). */
    setBranchOffer: (clinicId: string, branchId: string, serviceId: string, body: BranchOfferBody) =>
      c.send<void>('PUT', `${base(clinicId)}/branches/${seg(branchId)}/services/${seg(serviceId)}`, body),

    staff: (clinicId: string) => c.get<StaffMemberDto[]>(`${base(clinicId)}/staff`),
    updateStaff: (clinicId: string, membershipId: string, body: StaffBody) =>
      c.send<StaffMemberDto>('PUT', `${base(clinicId)}/staff/${seg(membershipId)}`, body),
    removeStaff: (clinicId: string, membershipId: string) => c.send<void>('DELETE', `${base(clinicId)}/staff/${seg(membershipId)}`),
    invitations: (clinicId: string) => c.get<InvitationDto[]>(`${base(clinicId)}/invitations`),
    invite: (clinicId: string, body: InvitationBody) => c.send<InvitationDto>('POST', `${base(clinicId)}/invitations`, body),
    revokeInvitation: (clinicId: string, id: string) => c.send<void>('DELETE', `${base(clinicId)}/invitations/${seg(id)}`),

    doctors: (clinicId: string) => c.get<DoctorDto[]>(`${base(clinicId)}/doctors`),
    createDoctor: (clinicId: string, body: DoctorBody) => c.send<DoctorDto>('POST', `${base(clinicId)}/doctors`, body),
    updateDoctor: (clinicId: string, doctorId: string, body: DoctorBody) =>
      c.send<DoctorDto>('PUT', `${base(clinicId)}/doctors/${seg(doctorId)}`, body),
    workingHours: (clinicId: string, doctorId: string) =>
      c.get<WorkingHoursEntry[]>(`${base(clinicId)}/doctors/${seg(doctorId)}/working-hours`),
    /** → 204; replaces the entries of the branches where the caller has schedule.manage. */
    setWorkingHours: (clinicId: string, doctorId: string, entries: WorkingHoursEntry[]) =>
      c.send<void>('PUT', `${base(clinicId)}/doctors/${seg(doctorId)}/working-hours`, entries),
    timeOff: (clinicId: string, doctorId: string, from: string, to: string) =>
      c.get<TimeOffDto[]>(`${base(clinicId)}/doctors/${seg(doctorId)}/time-off${query({ from, to })}`),
    addTimeOff: (clinicId: string, doctorId: string, body: { startsAt: string; endsAt: string; reason?: string }) =>
      c.send<TimeOffDto>('POST', `${base(clinicId)}/doctors/${seg(doctorId)}/time-off`, body),
    deleteTimeOff: (clinicId: string, doctorId: string, id: string) =>
      c.send<void>('DELETE', `${base(clinicId)}/doctors/${seg(doctorId)}/time-off/${seg(id)}`),

    appointments: (clinicId: string, filters: ClinicAppointmentFilters) =>
      c.get<Page<AppointmentDto>>(`${base(clinicId)}/appointments${query(filters)}`),
    appointment: (clinicId: string, id: string) => c.get<AppointmentDto>(`${base(clinicId)}/appointments/${seg(id)}`),
    eligibleDoctors: (clinicId: string, id: string) =>
      c.get<EligibleDoctor[]>(`${base(clinicId)}/appointments/${seg(id)}/eligible-doctors`),
    confirm: (clinicId: string, id: string, doctorId: string) =>
      c.send<AppointmentDto | undefined>('POST', `${base(clinicId)}/appointments/${seg(id)}/confirm`, { doctorId }),
    reject: (clinicId: string, id: string, reasonCode: string) =>
      c.send<AppointmentDto | undefined>('POST', `${base(clinicId)}/appointments/${seg(id)}/reject`, { reasonCode }),
    cancel: (clinicId: string, id: string, reasonCode: string) =>
      c.send<AppointmentDto | undefined>('POST', `${base(clinicId)}/appointments/${seg(id)}/cancel`, { reasonCode }),
    reschedule: (clinicId: string, id: string, body: { startsAt: string; doctorId: string }) =>
      c.send<AppointmentDto | undefined>('POST', `${base(clinicId)}/appointments/${seg(id)}/reschedule`, body),
    complete: (clinicId: string, id: string, chargedAmount: Money) =>
      c.send<AppointmentDto | undefined>('POST', `${base(clinicId)}/appointments/${seg(id)}/complete`, { chargedAmount }),
    noShow: (clinicId: string, id: string) => c.send<AppointmentDto | undefined>('POST', `${base(clinicId)}/appointments/${seg(id)}/no-show`),
    walkIn: (clinicId: string, body: WalkInBody, idempotencyKey: string) =>
      c.send<AppointmentDto>('POST', `${base(clinicId)}/appointments`, body, { idempotencyKey }),
    calendar: (clinicId: string, branchId: string, date: string) => c.get<CalendarDto>(`${base(clinicId)}/calendar${query({ branchId, date })}`),
  }
}
