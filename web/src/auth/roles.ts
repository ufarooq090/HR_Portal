/** Mirrors HrPortal.Domain.Security.Roles. UI checks only hide controls; the API enforces every rule. */
export const Roles = {
  SystemAdmin: 'SystemAdmin',
  HrAdmin: 'HrAdmin',
  HrExecutive: 'HrExecutive',
  HiringManager: 'HiringManager',
  Interviewer: 'Interviewer',
  LineManager: 'LineManager',
  Employee: 'Employee',
} as const

export type Role = (typeof Roles)[keyof typeof Roles]

export function hasAnyRole(user: { roles: readonly string[] } | undefined, ...roles: Role[]): boolean {
  return !!user && roles.some((r) => user.roles.includes(r))
}

type UserLike = { roles: readonly string[] } | undefined

/** Matches the API's ManageCountrySettings policy. */
export function canManageCountrySettings(user: UserLike): boolean {
  return hasAnyRole(user, Roles.SystemAdmin, Roles.HrAdmin)
}

/** Matches the API's ManageOrganisation policy (legal entities, offices, departments, grades). */
export function canManageOrganisation(user: UserLike): boolean {
  return hasAnyRole(user, Roles.HrAdmin)
}

/** HR staff add and import employees (EmployeeRecords Create in the permission matrix). */
export function canCreateEmployees(user: UserLike): boolean {
  return hasAnyRole(user, Roles.HrAdmin, Roles.HrExecutive)
}

/** Requisitions in the permission matrix: HR (full) and hiring/line managers (own department). */
export function canUseRequisitions(user: UserLike): boolean {
  return hasAnyRole(user, Roles.HrAdmin, Roles.HrExecutive, Roles.HiringManager, Roles.LineManager)
}

/** Candidate Tracker in the permission matrix: HR, hiring managers and interviewers. */
export function canUseCandidates(user: UserLike): boolean {
  return hasAnyRole(user, Roles.HrAdmin, Roles.HrExecutive, Roles.HiringManager, Roles.Interviewer)
}

/** Matches the API's ManageHrAccess policy. */
export function canManageHrAccess(user: UserLike): boolean {
  return hasAnyRole(user, Roles.HrAdmin)
}

/** Who gets the Settings section in the navigation. Everyone may read organisation data; these roles maintain it. */
export function showsSettings(user: UserLike): boolean {
  return hasAnyRole(user, Roles.SystemAdmin, Roles.HrAdmin, Roles.HrExecutive)
}

/**
 * Whether an HR Admin with this country scope may configure a country. Mirrors UserAccess.CanConfigureCountry;
 * System Admins configure country settings everywhere.
 */
export function canConfigureCountry(
  user: (NonNullable<UserLike> & { hrCountries: { all: boolean; codes: string[] } }) | undefined,
  countryCode: string,
): boolean {
  if (!user) return false
  if (user.roles.includes(Roles.SystemAdmin)) return true
  return user.roles.includes(Roles.HrAdmin) && (user.hrCountries.all || user.hrCountries.codes.includes(countryCode))
}

/** Resume Bank in the permission matrix: HR (their countries) and hiring managers (search and view, own country). */
export function canUseResumeBank(user: UserLike): boolean {
  return hasAnyRole(user, Roles.HrAdmin, Roles.HrExecutive, Roles.HiringManager)
}

/** HR import candidate sheets into the Resume Bank (ResumeBank Create). */
export function canImportCandidates(user: UserLike): boolean {
  return hasAnyRole(user, Roles.HrAdmin, Roles.HrExecutive)
}
