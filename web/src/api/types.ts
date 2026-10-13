export interface Me {
  userId: string
  displayName: string
  roles: string[]
  /** The signed-in user's employee record, if their account is linked to one. */
  employeeId: string | null
  countryCode: string | null
  /** Countries the user sees as HR staff (MC-03). Empty for non-HR users. */
  hrCountries: { all: boolean; codes: string[] }
}

export interface DevUser {
  id: string
  displayName: string
  email: string
  countryCode: string
  roles: string[]
}

export interface CountrySettings {
  countryCode: string
  name: string
  currencyCode: string
  defaultTimeZone: string
  probationMonths: number
  noticePeriodDays: number
}

export interface UpdateCountrySettingsRequest {
  name: string
  defaultTimeZone: string
  probationMonths: number
  noticePeriodDays: number
}

export interface CreateCountryRequest extends UpdateCountrySettingsRequest {
  countryCode: string
  currencyCode: string
}

export interface LegalEntity {
  id: string
  countryCode: string
  name: string
  isActive: boolean
}

export interface OfficeLocation {
  id: string
  countryCode: string
  name: string
  city: string | null
  /** Override; null means the country default applies. */
  timeZone: string | null
  effectiveTimeZone: string
  isActive: boolean
}

export interface Department {
  id: string
  code: string
  name: string
  isActive: boolean
}

export interface Grade {
  id: string
  countryCode: string
  code: string
  name: string
  level: number
  probationMonths: number | null
  noticePeriodDays: number | null
  effectiveProbationMonths: number
  effectiveNoticePeriodDays: number
  isActive: boolean
}

export interface EmployeeSummary {
  id: string
  employeeNumber: string
  fullName: string
  jobTitle: string
  workEmail: string
  countryCode: string
  departmentId: string
  departmentName: string
  officeLocationId: string
  officeLocationName: string
  managerId: string | null
  managerName: string | null
}

export interface HrAccess {
  employeeId: string
  employeeNumber: string
  employeeName: string
  employeeCountryCode: string
  allCountries: boolean
  countryCodes: string[]
  compensationAccess: boolean
}

export interface UpdateHrAccessRequest {
  allCountries: boolean
  countryCodes: string[]
  compensationAccess: boolean
}

/** RFC 9457 problem details as returned by the API. */
export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  errors?: Record<string, string[]>
}
