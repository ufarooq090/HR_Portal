import { describe, expect, it } from 'vitest'
import { clearDevUserId, DEV_USER_HEADER, devAuthHeaders, getDevUserId, setDevUserId } from './devAuth'
import { describeCountries } from '../pages/hrAccess'
import {
  canConfigureCountry,
  canManageCountrySettings,
  canManageHrAccess,
  canManageOrganisation,
  hasAnyRole,
  Roles,
  showsSettings,
} from './roles'

function memoryStorage() {
  const map = new Map<string, string>()
  return {
    getItem: (k: string) => map.get(k) ?? null,
    setItem: (k: string, v: string) => void map.set(k, v),
    removeItem: (k: string) => void map.delete(k),
  }
}

describe('devAuth', () => {
  it('remembers and forgets the selected dev user', () => {
    const storage = memoryStorage()
    expect(getDevUserId(storage)).toBeNull()

    setDevUserId('dev-us-hradmin', storage)
    expect(getDevUserId(storage)).toBe('dev-us-hradmin')

    clearDevUserId(storage)
    expect(getDevUserId(storage)).toBeNull()
  })

  it('survives storage that throws', () => {
    const broken = {
      getItem: () => {
        throw new Error('blocked')
      },
      setItem: () => {
        throw new Error('blocked')
      },
      removeItem: () => {
        throw new Error('blocked')
      },
    }
    expect(getDevUserId(broken)).toBeNull()
    expect(() => setDevUserId('x', broken)).not.toThrow()
  })

  it('builds the header the API expects', () => {
    expect(devAuthHeaders('dev-pk-employee')).toEqual({ [DEV_USER_HEADER]: 'dev-pk-employee' })
    expect(DEV_USER_HEADER).toBe('X-Dev-User')
  })
})

describe('roles', () => {
  it('only HR admins and system admins can manage country settings', () => {
    expect(canManageCountrySettings({ roles: [Roles.HrAdmin, Roles.Employee] })).toBe(true)
    expect(canManageCountrySettings({ roles: [Roles.SystemAdmin] })).toBe(true)
    expect(canManageCountrySettings({ roles: [Roles.HrExecutive, Roles.Employee] })).toBe(false)
    expect(canManageCountrySettings(undefined)).toBe(false)
  })

  it('only HR admins manage organisation structure and HR access', () => {
    expect(canManageOrganisation({ roles: [Roles.HrAdmin] })).toBe(true)
    expect(canManageOrganisation({ roles: [Roles.SystemAdmin, Roles.HrExecutive] })).toBe(false)
    expect(canManageHrAccess({ roles: [Roles.HrAdmin] })).toBe(true)
    expect(canManageHrAccess({ roles: [Roles.HrExecutive, Roles.LineManager] })).toBe(false)
  })

  it('shows settings to admins and HR staff only', () => {
    expect(showsSettings({ roles: [Roles.HrExecutive, Roles.Employee] })).toBe(true)
    expect(showsSettings({ roles: [Roles.SystemAdmin] })).toBe(true)
    expect(showsSettings({ roles: [Roles.LineManager, Roles.Employee] })).toBe(false)
  })

  it('MC-03: HR admins configure only countries in their scope; system admins configure all', () => {
    const limited = { roles: [Roles.HrAdmin], hrCountries: { all: false, codes: ['PK'] } }
    expect(canConfigureCountry(limited, 'PK')).toBe(true)
    expect(canConfigureCountry(limited, 'US')).toBe(false)
    expect(canConfigureCountry({ roles: [Roles.HrAdmin], hrCountries: { all: true, codes: [] } }, 'US')).toBe(true)
    expect(canConfigureCountry({ roles: [Roles.SystemAdmin], hrCountries: { all: false, codes: [] } }, 'US')).toBe(true)
    expect(canConfigureCountry({ roles: [Roles.HrExecutive], hrCountries: { all: true, codes: [] } }, 'PK')).toBe(false)
    expect(canConfigureCountry(undefined, 'PK')).toBe(false)
  })

  it('describes an HR country scope', () => {
    expect(describeCountries({ allCountries: true, countryCodes: [] })).toBe('All countries')
    expect(describeCountries({ allCountries: false, countryCodes: ['US', 'PK'] })).toBe('PK, US')
    expect(describeCountries({ allCountries: false, countryCodes: [] })).toBe('No countries')
  })

  it('hasAnyRole matches any of the given roles', () => {
    expect(hasAnyRole({ roles: [Roles.LineManager] }, Roles.HiringManager, Roles.LineManager)).toBe(true)
    expect(hasAnyRole({ roles: [] }, Roles.Employee)).toBe(false)
  })
})
