import { describe, expect, it } from 'vitest'
import { fieldsIn, type MergeField, templatesUrl, unknownFields } from '../../api/templates'
import { canManageTemplates, canUseTemplates, isHr, Roles } from '../../auth/roles'

const field = (key: string): MergeField => ({ key, label: key, description: '', recordTypes: ['Application'] })

describe('ET-02 merge fields', () => {
  it('lists the fields a text uses, once each, in order', () => {
    expect(fieldsIn('Hi {{first_name}}, {{ Designation }} on {{interview_date}} – {{first_name}}')).toEqual(['first_name', 'designation', 'interview_date'])
    expect(fieldsIn('No fields {here} or {{ }}')).toEqual([])
  })

  it('flags fields the record type cannot fill', () => {
    expect(unknownFields('<p>{{first_name}} {{probation_end_date}} {{frist_name}}</p>', [field('first_name')])).toEqual(['probation_end_date', 'frist_name'])
  })
})

describe('ET templates list', () => {
  it('filters by record type, country and active', () => {
    expect(templatesUrl()).toBe('/api/email-templates')
    expect(templatesUrl({ recordType: 'Employee', country: 'PK', activeOnly: true })).toBe('/api/email-templates?recordType=Employee&country=PK&activeOnly=true')
  })
})

describe('Email templates access', () => {
  it('HR Admins manage; HR and hiring managers send; others neither', () => {
    const user = (...roles: string[]) => ({ roles })
    expect(canManageTemplates(user(Roles.HrAdmin))).toBe(true)
    expect(canManageTemplates(user(Roles.HrExecutive))).toBe(false)
    expect(canUseTemplates(user(Roles.HiringManager))).toBe(true)
    expect(canUseTemplates(user(Roles.LineManager, Roles.Interviewer, Roles.Employee))).toBe(false)
    expect(isHr(user(Roles.HiringManager))).toBe(false)
  })
})
