import type { EmploymentStatus } from '../../api/types'

/** "OnNotice" → "On notice", "IdProof" → "ID proof". */
export function label(value: string): string {
  const words = value.replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase()
  const text = words.replace(/\bid\b/g, 'ID')
  return text.charAt(0).toUpperCase() + text.slice(1)
}

/**
 * Calendar dates (DateOnly, "2024-01-15") as "15 Jan 2024". Formatted in UTC so the day never shifts with the
 * viewer's time zone; these dates have no time of day.
 */
export function formatDate(isoDate: string | null | undefined): string {
  if (!isoDate) return '—'
  const date = new Date(`${isoDate.slice(0, 10)}T00:00:00Z`)
  if (Number.isNaN(date.getTime())) return isoDate
  return new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' }).format(date)
}

/** Money always shows its currency (PKR or USD), never a bare number. */
export function formatMoney(amount: number, currency: string): string {
  return new Intl.NumberFormat('en', { style: 'currency', currency, currencyDisplay: 'code', maximumFractionDigits: 2 })
    .format(amount)
    .replace(/ /g, ' ')
}

export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

export type BadgeColor = 'success' | 'warning' | 'danger' | 'informative' | 'subtle'

export function statusColor(status: EmploymentStatus): BadgeColor {
  switch (status) {
    case 'Confirmed':
      return 'success'
    case 'Probation':
      return 'informative'
    case 'OnNotice':
      return 'warning'
    case 'Terminated':
    case 'Absconded':
      return 'danger'
    default:
      return 'subtle'
  }
}

/** Today's calendar date in the user's time zone, as YYYY-MM-DD for date inputs. */
export function todayIso(now = new Date()): string {
  const offset = now.getTimezoneOffset() * 60_000
  return new Date(now.getTime() - offset).toISOString().slice(0, 10)
}
