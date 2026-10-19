import { createElement, lazy, Suspense, type ReactNode } from 'react'
import type { RouteObject } from 'react-router'
import { LoginPage } from '../auth/LoginPage'
import { RequireAuth, RequirePermission, RequirePlatform, RequirePlatformPermission } from '../auth/guards'
import { TelegramCallbackPage } from '../auth/TelegramCallbackPage'
import { Perm, PlatformPerm } from '../features/clinic/permissions'
import { ClinicPage } from '../features/public/ClinicPage'
import { CatalogPage } from '../features/public/CatalogPage'
import { LandingPage } from '../features/public/LandingPage'
import { NotFoundPage } from '../ui/states'
import { from } from './lazyRoute'
import { PublicLayout } from './PublicLayout'

const patient = from(() => import('./chunks/patient'))
const clinic = from(() => import('./chunks/clinic'))
const admin = from(() => import('./chunks/admin'))
const tg = from(() => import('./chunks/telegram'))
// Defined only in development so the gallery chunk is not emitted in production builds.
const devGallery = import.meta.env.DEV ? lazy(() => import('../dev/UiGallery').then((m) => ({ default: m.UiGallery }))) : null

const perm = (anyOf: string[], element: ReactNode) => <RequirePermission anyOf={anyOf}>{element}</RequirePermission>
const platform = (anyOf: string[], element: ReactNode) => <RequirePlatformPermission anyOf={anyOf}>{element}</RequirePlatformPermission>

/** Route map — documented in docs/FRONTEND.md. */
export const routes: RouteObject[] = [
  {
    element: <PublicLayout />,
    children: [
      { index: true, element: <LandingPage /> },
      { path: 'clinics', element: <CatalogPage /> },
      { path: 'clinics/:clinicId', element: <ClinicPage /> },
      { path: 'book/:clinicId', element: patient('BookingPage') },
      { path: 'login', element: <LoginPage /> },
      { path: 'login/telegram', element: <TelegramCallbackPage /> },
      // Development-only component gallery; the branch is removed from production builds.
      ...(devGallery ? [{ path: '__ui', element: <Suspense>{createElement(devGallery)}</Suspense> }] : []),
      {
        element: <RequireAuth />,
        children: [
          {
            path: 'me',
            element: patient('AccountLayout'),
            children: [
              { index: true, element: patient('ProfilePage') },
              { path: 'security', element: patient('SecurityPage') },
              { path: 'invitations', element: patient('InvitationsPage') },
            ],
          },
          { path: 'me/appointments', element: patient('MyAppointmentsPage') },
          { path: 'me/appointments/:id', element: patient('AppointmentDetailPage') },
          { path: 'clinic', element: patient('ClinicChooserPage') },
          { path: 'clinic/register', element: patient('RegisterClinicPage') },
        ],
      },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
  {
    path: 'clinic/:clinicId',
    element: (
      <RequireAuth>
        {clinic('ClinicLayout')}
      </RequireAuth>
    ),
    children: [
      { index: true, element: clinic('DashboardPage') },
      { path: 'requests', element: perm([Perm.triage], clinic('RequestsPage')) },
      { path: 'appointments', element: perm([Perm.appointmentView], clinic('AppointmentsPage')) },
      { path: 'appointments/:appointmentId', element: perm([Perm.appointmentView], clinic('ClinicAppointmentPage')) },
      { path: 'calendar', element: perm([Perm.appointmentView], clinic('CalendarPage')) },
      { path: 'walk-in', element: perm([Perm.walkIn], clinic('WalkInPage')) },
      { path: 'doctors', element: perm([Perm.scheduleManage, Perm.ownTimeOff, Perm.catalogManage], clinic('DoctorsPage')) },
      { path: 'doctors/:doctorId', element: perm([Perm.scheduleManage, Perm.ownTimeOff, Perm.catalogManage], clinic('DoctorDetailPage')) },
      { path: 'services', element: perm([Perm.catalogManage], clinic('ServicesPage')) },
      { path: 'branches', element: perm([Perm.branchManage], clinic('BranchesPage')) },
      { path: 'staff', element: perm([Perm.staffManage], clinic('StaffPage')) },
      { path: 'payments', element: perm([Perm.paymentRecord, Perm.reportView], clinic('PaymentsPage')) },
      { path: 'reports', element: perm([Perm.reportView], clinic('ReportsPage')) },
      { path: 'settlements', element: perm([Perm.settlementView], clinic('SettlementsPage')) },
      { path: 'audit', element: perm([Perm.auditView], clinic('ClinicAuditPage')) },
      { path: 'profile', element: clinic('ClinicProfilePage') },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
  {
    path: 'admin',
    element: (
      <RequireAuth>
        <RequirePlatform>
          {admin('AdminLayout')}
        </RequirePlatform>
      </RequireAuth>
    ),
    children: [
      { index: true, element: admin('AdminDashboardPage') },
      { path: 'clinics', element: platform([PlatformPerm.clinicVerify], admin('AdminClinicsPage')) },
      { path: 'users', element: platform([PlatformPerm.accountSuspend], admin('AdminUsersPage')) },
      { path: 'reports', element: platform([PlatformPerm.reportsHandle], admin('AdminReportsPage')) },
      { path: 'reviews', element: platform([PlatformPerm.reportsHandle], admin('AdminReviewsPage')) },
      { path: 'commission', element: platform([PlatformPerm.financeSettle], admin('CommissionPage')) },
      { path: 'settlements', element: platform([PlatformPerm.financeSettle], admin('AdminSettlementsPage')) },
      { path: 'tenant-data', element: platform([PlatformPerm.tenantRead], admin('TenantDataPage')) },
      { path: 'audit', element: platform([PlatformPerm.auditView], admin('AdminAuditPage')) },
      { path: 'notifications', element: platform([PlatformPerm.auditView], admin('NotificationsPage')) },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
  {
    path: 'tg',
    element: tg('TgRoot'),
    children: [
      { index: true, element: tg('TgHome') },
      { path: 'clinics/:clinicId', element: tg('TgBook') },
      { path: 'appointments', element: tg('TgMine') },
      { path: 'appointments/:id', element: tg('TgAppointment') },
      { path: 'staff', element: tg('TgStaff') },
    ],
  },
]
