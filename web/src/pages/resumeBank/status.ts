import type { ResumeBankStatus } from '../../api/types'
import type { BadgeColor } from '../employees/format'

export function statusBadgeColor(status: ResumeBankStatus): BadgeColor {
  switch (status) {
    case 'Shortlisted':
    case 'Hired':
      return 'success'
    case 'Blacklisted':
    case 'Terminated':
    case 'Absconded':
      return 'danger'
    case 'NoShow':
    case 'OfferDeclined':
      return 'warning'
    default:
      return 'subtle'
  }
}
