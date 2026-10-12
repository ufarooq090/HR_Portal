export interface Me {
  userId: string
  displayName: string
  roles: string[]
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
  probationMonths: number
  noticePeriodDays: number
}

export interface UpdateCountrySettingsRequest {
  probationMonths: number
  noticePeriodDays: number
}

/** RFC 9457 problem details as returned by the API. */
export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  errors?: Record<string, string[]>
}
