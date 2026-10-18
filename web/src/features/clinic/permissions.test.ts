import { describe, expect, it } from 'vitest'
import { adminMenu, clinicMenu, grantableRoles, visibleMenu } from './permissions'

const paths = (perms: string[], menu = clinicMenu) => visibleMenu(menu, perms).map((i) => i.path)

describe('menu filtering by effective permissions', () => {
  it('shows only sections the membership permissions allow', () => {
    const receptionist = ['schedule.manage', 'appointment.view', 'appointment.triage', 'appointment.cancel', 'appointment.outcome', 'appointment.create_walkin', 'payment.record']
    expect(paths(receptionist)).toEqual(['', 'requests', 'appointments', 'calendar', 'walk-in', 'doctors', 'payments', 'profile'])
  })

  it('gives a doctor with only own-time-off the dashboard, calendar and doctors', () => {
    expect(paths(['appointment.view', 'schedule.own_timeoff', 'appointment.outcome'])).toEqual(['', 'appointments', 'calendar', 'doctors', 'profile'])
  })

  it('gives a finance manager finance sections but not staff or catalogue', () => {
    const p = paths(['appointment.view', 'payment.record', 'payment.refund', 'finance.report.view', 'finance.settlement.view'])
    expect(p).toEqual(expect.arrayContaining(['payments', 'reports', 'settlements']))
    expect(p).not.toContain('staff')
    expect(p).not.toContain('services')
    expect(p).not.toContain('requests')
  })

  it('does not depend on role names: an unknown role with permissions still gets the sections', () => {
    expect(paths(['audit.view'])).toContain('audit')
  })

  it('filters the admin menu by platform permissions', () => {
    expect(paths(['platform.finance.settle'], adminMenu)).toEqual(['', 'commission', 'settlements'])
  })

  it('lets only owners grant owner/admin roles', () => {
    expect(grantableRoles('ClinicOwner')).toContain('ClinicAdmin')
    expect(grantableRoles('ClinicAdmin')).toEqual(['Receptionist', 'Doctor', 'FinanceManager'])
  })
})
