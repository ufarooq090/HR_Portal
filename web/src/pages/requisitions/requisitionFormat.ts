import type { ApprovalDecision, RequisitionStatus } from '../../api/types'
import type { BadgeColor } from '../employees/format'

export function requisitionStatusColor(status: RequisitionStatus): BadgeColor {
  switch (status) {
    case 'Open':
      return 'success'
    case 'PendingApproval':
      return 'warning'
    case 'OnHold':
      return 'informative'
    case 'Cancelled':
      return 'danger'
    default:
      return 'subtle'
  }
}

export function decisionColor(decision: ApprovalDecision): BadgeColor {
  return decision === 'Approved' ? 'success' : decision === 'Rejected' ? 'danger' : decision === 'Pending' ? 'warning' : 'subtle'
}

/** Button text for a manual status change. */
export function statusActionLabel(status: RequisitionStatus): string {
  switch (status) {
    case 'OnHold':
      return 'Put on hold'
    case 'Open':
      return 'Reopen'
    case 'Filled':
      return 'Mark filled'
    case 'Cancelled':
      return 'Cancel requisition'
    default:
      return status
  }
}

/** "PKR 2,000,000 – 3,000,000": one currency, never converted (MC-02). */
export function budgetRange(min: number, max: number, currency: string): string {
  const number = new Intl.NumberFormat('en', { maximumFractionDigits: 2 })
  return `${currency} ${number.format(min)} – ${number.format(max)}`
}
