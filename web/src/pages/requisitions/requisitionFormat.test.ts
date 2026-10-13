import { describe, expect, it } from 'vitest'
import { requisitionsUrl } from '../../api/requisitions'
import { canUseRequisitions, Roles } from '../../auth/roles'
import { budgetRange, decisionColor, requisitionStatusColor, statusActionLabel } from './requisitionFormat'

describe('requisitions', () => {
  it('MC-02: shows a budget range in one currency, never converted', () => {
    expect(budgetRange(2_000_000, 3_000_000, 'PKR')).toBe('PKR 2,000,000 – 3,000,000')
    expect(budgetRange(90_000.5, 120_000, 'USD')).toBe('USD 90,000.5 – 120,000')
  })

  it('names the manual status actions', () => {
    expect(statusActionLabel('OnHold')).toBe('Put on hold')
    expect(statusActionLabel('Open')).toBe('Reopen')
    expect(statusActionLabel('Filled')).toBe('Mark filled')
    expect(statusActionLabel('Cancelled')).toBe('Cancel requisition')
  })

  it('colours statuses and decisions by meaning', () => {
    expect(requisitionStatusColor('Open')).toBe('success')
    expect(requisitionStatusColor('PendingApproval')).toBe('warning')
    expect(decisionColor('Rejected')).toBe('danger')
    expect(decisionColor('Closed')).toBe('subtle')
  })

  it('builds the list URL with repeated status filters', () => {
    expect(requisitionsUrl()).toBe('/api/requisitions')
    expect(requisitionsUrl(['Open', 'OnHold'])).toBe('/api/requisitions?status=Open&status=OnHold')
  })

  it('REQ-01: HR and managers use requisitions; employees and interviewers do not', () => {
    expect(canUseRequisitions({ roles: [Roles.LineManager, Roles.Employee] })).toBe(true)
    expect(canUseRequisitions({ roles: [Roles.HrExecutive] })).toBe(true)
    expect(canUseRequisitions({ roles: [Roles.Interviewer, Roles.Employee] })).toBe(false)
    expect(canUseRequisitions({ roles: [Roles.SystemAdmin] })).toBe(false)
  })
})
