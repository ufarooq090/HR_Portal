import { describe, expect, it } from 'vitest'
import { type FieldDef, initialValues, missingRequired, toRequestBody } from './forms'

const gradeFields: FieldDef[] = [
  { name: 'countryCode', label: 'Country', kind: 'country', required: true, createOnly: true },
  { name: 'code', label: 'Code', kind: 'code', required: true },
  { name: 'name', label: 'Name', kind: 'text', required: true },
  { name: 'level', label: 'Level', kind: 'number', required: true },
  { name: 'probationMonths', label: 'Probation', kind: 'optionalNumber' },
  { name: 'city', label: 'City', kind: 'text' },
  { name: 'isActive', label: 'Active', kind: 'active' },
]

describe('initialValues', () => {
  it('starts new items empty and active', () => {
    expect(initialValues(gradeFields)).toEqual({
      countryCode: '',
      code: '',
      name: '',
      level: '',
      probationMonths: '',
      city: '',
      isActive: true,
    })
  })

  it('turns an existing item into form strings', () => {
    const values = initialValues(gradeFields, {
      countryCode: 'PK',
      code: 'G3',
      name: 'Manager',
      level: 3,
      probationMonths: null,
      city: null,
      isActive: false,
    })
    expect(values).toMatchObject({ level: '3', probationMonths: '', isActive: false })
  })
})

describe('toRequestBody', () => {
  const values = {
    countryCode: 'pk',
    code: ' g3 ',
    name: ' Manager ',
    level: '3',
    probationMonths: '',
    city: '  ',
    isActive: false,
  }

  it('builds a create request: codes upper-cased, blanks become null, no active flag', () => {
    expect(toRequestBody(gradeFields, values, 'create')).toEqual({
      countryCode: 'PK',
      code: 'G3',
      name: 'Manager',
      level: 3,
      probationMonths: null,
      city: null,
    })
  })

  it('builds an update request without create-only fields', () => {
    const body = toRequestBody(gradeFields, { ...values, probationMonths: '6' }, 'edit')
    expect(body).not.toHaveProperty('countryCode')
    expect(body).toMatchObject({ probationMonths: 6, isActive: false })
  })

  it('sends null for an optional number that is not a whole number, so the API reports it', () => {
    expect(toRequestBody(gradeFields, { ...values, probationMonths: '2.5' }, 'create').probationMonths).toBeNull()
  })
})

describe('missingRequired', () => {
  it('lists empty required fields, ignoring create-only ones when editing', () => {
    const empty = initialValues(gradeFields)
    expect(missingRequired(gradeFields, empty, 'create')).toEqual(['countryCode', 'code', 'name', 'level'])
    expect(missingRequired(gradeFields, empty, 'edit')).toEqual(['code', 'name', 'level'])
  })
})
