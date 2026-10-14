import { describe, expect, it } from 'vitest'
import { employeesUrl } from '../../api/employees'
import { formatBytes, formatDate, formatDateTime, formatDay, formatMoney, label, parseDate, statusColor, todayIso } from './format'

describe('employee formatting', () => {
  it('turns enum values into readable labels', () => {
    expect(label('OnNotice')).toBe('On notice')
    expect(label('IdProof')).toBe('ID proof')
    expect(label('ManagerChange')).toBe('Manager change')
    expect(label('Confirmed')).toBe('Confirmed')
  })

  it('formats calendar dates without shifting the day', () => {
    expect(formatDate('2024-01-15')).toBe('15 Jan 2024')
    expect(formatDate('2026-09-30')).toBe('30 Sep 2026')
    expect(formatDate(null)).toBe('—')
  })

  it("formats timestamps in the viewer's time zone with an unambiguous date", () => {
    const local = new Date(2026, 9, 14, 9, 5)
    expect(formatDay(local.toISOString())).toBe('14 Oct 2026')
    expect(formatDateTime(local.toISOString())).toBe('14 Oct 2026, 09:05')
    expect(formatDateTime(null)).toBe('—')
  })

  it('reads typed dates with a month name or in ISO form, and refuses ambiguous ones', () => {
    expect(parseDate('14 Oct 2026')).toBe('2026-10-14')
    expect(parseDate(' 3 september 2025 ')).toBe('2025-09-03')
    expect(parseDate('3 Sept 2025')).toBe('2025-09-03')
    expect(parseDate('14-Oct-2026')).toBe('2026-10-14')
    expect(parseDate('2026-10-14')).toBe('2026-10-14')
    expect(parseDate('10/14/2026')).toBeNull()
    expect(parseDate('31 Feb 2026')).toBeNull()
    expect(parseDate('14 Oc 2026')).toBeNull()
    expect(parseDate('')).toBeNull()
  })

  it('MC-02: always shows the currency with money, in whole units for both currencies', () => {
    expect(formatMoney(2400000, 'PKR')).toBe('PKR 2,400,000')
    expect(formatMoney(85000, 'USD')).toBe('USD 85,000')
    expect(formatMoney(85000.4, 'USD')).toBe('USD 85,000')
    expect(formatMoney(3500000.75, 'PKR')).toBe('PKR 3,500,001')
  })

  it('formats file sizes', () => {
    expect(formatBytes(512)).toBe('512 B')
    expect(formatBytes(2048)).toBe('2 KB')
    expect(formatBytes(3 * 1024 * 1024)).toBe('3.0 MB')
  })

  it('colours statuses by meaning', () => {
    expect(statusColor('Confirmed')).toBe('success')
    expect(statusColor('OnNotice')).toBe('warning')
    expect(statusColor('Terminated')).toBe('danger')
    expect(statusColor('Resigned')).toBe('subtle')
  })

  it('gives today as a local YYYY-MM-DD date', () => {
    expect(todayIso(new Date(2026, 9, 13, 23, 30))).toBe('2026-10-13')
  })
})

describe('EMP-08 directory query', () => {
  it('leaves out empty filters and repeats status', () => {
    expect(employeesUrl()).toBe('/api/employees')
    expect(employeesUrl({ search: '  ', departmentId: '' })).toBe('/api/employees')
    expect(employeesUrl({ search: 'ali', countryCode: 'PK', status: ['Probation', 'OnNotice'] })).toBe(
      '/api/employees?search=ali&countryCode=PK&status=Probation&status=OnNotice',
    )
  })
})
