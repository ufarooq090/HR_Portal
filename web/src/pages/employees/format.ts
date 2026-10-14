import type { EmploymentStatus } from '../../api/types'

/** "OnNotice" → "On notice", "IdProof" → "ID proof". */
export function label(value: string): string {
  const words = value.replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase()
  const text = words.replace(/\bid\b/g, 'ID')
  return text.charAt(0).toUpperCase() + text.slice(1)
}

const fullMonths = ['january', 'february', 'march', 'april', 'may', 'june', 'july', 'august', 'september', 'october', 'november', 'december']
const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']

/** One unambiguous date format everywhere, "14 Oct 2026", whatever the browser's locale. */
function dayMonthYear(year: number, monthIndex: number, day: number): string {
  return `${day} ${months[monthIndex]} ${year}`
}

/**
 * Calendar dates (DateOnly, "2024-01-15") as "15 Jan 2024". Read in UTC so the day never shifts with the
 * viewer's time zone; these dates have no time of day.
 */
export function formatDate(isoDate: string | null | undefined): string {
  if (!isoDate) return '—'
  const date = new Date(`${isoDate.slice(0, 10)}T00:00:00Z`)
  if (Number.isNaN(date.getTime())) return isoDate
  return dayMonthYear(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate())
}

/** The day of a UTC timestamp in the viewer's time zone, e.g. "14 Oct 2026". */
export function formatDay(utc: string | null | undefined): string {
  if (!utc) return '—'
  const date = new Date(utc)
  if (Number.isNaN(date.getTime())) return utc
  return dayMonthYear(date.getFullYear(), date.getMonth(), date.getDate())
}

/** A UTC timestamp in the viewer's time zone, e.g. "14 Oct 2026, 09:05" (24-hour clock). */
export function formatDateTime(utc: string | null | undefined): string {
  if (!utc) return '—'
  const date = new Date(utc)
  if (Number.isNaN(date.getTime())) return utc
  const time = `${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`
  return `${formatDay(utc)}, ${time}`
}

/**
 * Reads a typed date: "14 Oct 2026", "14 October 2026", "14-Oct-2026" or ISO "2026-10-14". Returns ISO
 * "YYYY-MM-DD", or null when the text is not a real date. Numeric day/month orders such as 10/14/2026 are
 * refused because they are ambiguous between Pakistan and the US.
 */
export function parseDate(text: string): string | null {
  const value = text.trim()
  let year: number, month: number, day: number
  const iso = /^(\d{4})-(\d{1,2})-(\d{1,2})$/.exec(value)
  const named = /^(\d{1,2})[\s-]+([a-z]+)\.?,?[\s-]+(\d{4})$/i.exec(value)
  if (iso) {
    ;[year, month, day] = [Number(iso[1]), Number(iso[2]) - 1, Number(iso[3])]
  } else if (named) {
    const name = named[2]!.toLowerCase()
    month = name.length < 3 ? -1 : name === 'sept' ? 8 : fullMonths.findIndex((m) => m.startsWith(name))
    if (month < 0) return null
    ;[year, day] = [Number(named[3]), Number(named[1])]
  } else {
    return null
  }
  const date = new Date(Date.UTC(year, month, day))
  if (date.getUTCFullYear() !== year || date.getUTCMonth() !== month || date.getUTCDate() !== day) return null
  return `${year}-${String(month + 1).padStart(2, '0')}-${String(day).padStart(2, '0')}`
}

/** Money always shows its currency (PKR or USD), never a bare number. Salaries show whole units: "PKR 2,400,000". */
export function formatMoney(amount: number, currency: string): string {
  return new Intl.NumberFormat('en', { style: 'currency', currency, currencyDisplay: 'code', maximumFractionDigits: 0 })
    .format(amount)
    .replace(/\s/g, ' ')
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
