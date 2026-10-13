import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { anonymousApi, api } from './client'
import type {
  CountrySettings,
  CreateCountryRequest,
  DevUser,
  EmployeeSummary,
  HrAccess,
  Me,
  UpdateCountrySettingsRequest,
  UpdateHrAccessRequest,
} from './types'

export const queryKeys = {
  me: ['me'] as const,
  devUsers: ['dev', 'users'] as const,
  countries: ['organisation', 'countries'] as const,
  organisation: (resource: OrganisationResource) => ['organisation', resource] as const,
  employees: (search: string) => ['employees', search] as const,
  hrAccess: ['access', 'hr'] as const,
}

export function useMe() {
  return useQuery({ queryKey: queryKeys.me, queryFn: () => api.get<Me>('/api/me') })
}

export function useDevUsers() {
  return useQuery({ queryKey: queryKeys.devUsers, queryFn: () => anonymousApi.get<DevUser[]>('/api/dev/users') })
}

export function useCountries() {
  return useQuery({
    queryKey: queryKeys.countries,
    queryFn: () => api.get<CountrySettings[]>('/api/organisation/countries'),
  })
}

export function useCreateCountry() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: CreateCountryRequest) => api.post<CountrySettings>('/api/organisation/countries', body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.countries }),
  })
}

export function useUpdateCountry() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ countryCode, ...body }: UpdateCountrySettingsRequest & { countryCode: string }) =>
      api.put<CountrySettings>(`/api/organisation/countries/${encodeURIComponent(countryCode)}`, body),
    // Offices and grades show values inherited from the country.
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['organisation'] }),
  })
}

/** Organisation structure managed by HR Admins: same list/create/update/delete shape for each. */
export type OrganisationResource = 'legal-entities' | 'locations' | 'departments' | 'grades'

export function useOrganisationItems<T>(resource: OrganisationResource) {
  return useQuery({
    queryKey: queryKeys.organisation(resource),
    queryFn: () => api.get<T[]>(`/api/organisation/${resource}`),
  })
}

export function useSaveOrganisationItem<T>(resource: OrganisationResource) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id?: string; body: unknown }) =>
      id
        ? api.put<T>(`/api/organisation/${resource}/${encodeURIComponent(id)}`, body)
        : api.post<T>(`/api/organisation/${resource}`, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.organisation(resource) }),
  })
}

export function useDeleteOrganisationItem(resource: OrganisationResource) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.delete(`/api/organisation/${resource}/${encodeURIComponent(id)}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.organisation(resource) }),
  })
}

export function useEmployees(search = '') {
  return useQuery({
    queryKey: queryKeys.employees(search),
    queryFn: () =>
      api.get<EmployeeSummary[]>(search ? `/api/employees?search=${encodeURIComponent(search)}` : '/api/employees'),
  })
}

export function useHrAccess() {
  return useQuery({ queryKey: queryKeys.hrAccess, queryFn: () => api.get<HrAccess[]>('/api/access/hr') })
}

export function useSaveHrAccess() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ employeeId, ...body }: UpdateHrAccessRequest & { employeeId: string }) =>
      api.put<HrAccess>(`/api/access/hr/${encodeURIComponent(employeeId)}`, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.hrAccess }),
  })
}

export function useResetHrAccess() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (employeeId: string) => api.delete(`/api/access/hr/${encodeURIComponent(employeeId)}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.hrAccess }),
  })
}
