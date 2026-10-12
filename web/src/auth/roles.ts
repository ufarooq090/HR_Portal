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

/** Matches the API's ManageCountrySettings policy. */
export function canManageCountrySettings(user: { roles: readonly string[] } | undefined): boolean {
  return hasAnyRole(user, Roles.SystemAdmin, Roles.HrAdmin)
}
