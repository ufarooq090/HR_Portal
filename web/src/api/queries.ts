import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { anonymousApi, api } from './client'
import type { CountrySettings, DevUser, Me, UpdateCountrySettingsRequest } from './types'

export const queryKeys = {
  me: ['me'] as const,
  devUsers: ['dev', 'users'] as const,
  countries: ['organisation', 'countries'] as const,
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

export function useUpdateCountry() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ countryCode, ...body }: UpdateCountrySettingsRequest & { countryCode: string }) =>
      api.put<CountrySettings>(`/api/organisation/countries/${encodeURIComponent(countryCode)}`, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.countries }),
  })
}
