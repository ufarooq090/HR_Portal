import { describe, expect, it } from 'vitest'
import { parseList, resumeBankUrl, statusLabels } from '../../api/resumeBank'
import { resumeBankStatuses } from '../../api/types'
import { canImportCandidates, canUseResumeBank, Roles } from '../../auth/roles'
import { statusBadgeColor } from './status'

describe('RB-04 Resume Bank search', () => {
  it('sends only the filters that are set', () => {
    expect(resumeBankUrl()).toBe('/api/resume-bank')
    expect(resumeBankUrl({ q: ' "data engineer" spark ', skills: '', status: '', page: 2 })).toBe('/api/resume-bank?q=%22data+engineer%22+spark&page=2')
    expect(resumeBankUrl({ skills: 'python, sql', minExperience: '3', status: 'Shortlisted' })).toBe(
      '/api/resume-bank?skills=python%2C+sql&minExperience=3&status=Shortlisted',
    )
  })

  it('drops the currency when there is no salary limit', () => {
    expect(resumeBankUrl({ ctcCurrency: 'PKR' })).toBe('/api/resume-bank')
    expect(resumeBankUrl({ maxCurrentCtc: '2000000', ctcCurrency: 'PKR' })).toBe('/api/resume-bank?maxCurrentCtc=2000000&ctcCurrency=PKR')
  })

  it('splits skills and tags like the server stores them', () => {
    expect(parseList(' Python; SQL, python ,, Spark ')).toEqual(['python', 'sql', 'spark'])
    expect(parseList('')).toEqual([])
  })
})

describe('RB-03 statuses', () => {
  it('has a label and colour for every status', () => {
    expect(resumeBankStatuses.map((s) => statusLabels[s])).toEqual([
      'Shortlisted',
      'Not shortlisted',
      'No-show',
      'Offer declined',
      'Resigned',
      'Terminated',
      'Absconded',
      'Hired',
      'Blacklisted',
    ])
    expect(statusBadgeColor('Blacklisted')).toBe('danger')
    expect(statusBadgeColor('Absconded')).toBe('danger')
    expect(statusBadgeColor('Hired')).toBe('success')
    expect(statusBadgeColor('Resigned')).toBe('subtle')
  })
})

describe('Resume Bank access', () => {
  it('is for HR and hiring managers; only HR import', () => {
    const user = (...roles: string[]) => ({ roles })
    expect(canUseResumeBank(user(Roles.HiringManager))).toBe(true)
    expect(canUseResumeBank(user(Roles.HrExecutive))).toBe(true)
    expect(canUseResumeBank(user(Roles.LineManager, Roles.Interviewer, Roles.Employee))).toBe(false)
    expect(canImportCandidates(user(Roles.HiringManager))).toBe(false)
    expect(canImportCandidates(user(Roles.HrAdmin))).toBe(true)
  })
})
