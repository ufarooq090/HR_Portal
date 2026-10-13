import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './client'
import type { ImportResult, ResumeBankDetail, ResumeBankSearchResult, ResumeBankStatus } from './types'

export const resumeBankKeys = {
  all: ['resume-bank'] as const,
  search: (url: string) => ['resume-bank', 'search', url] as const,
  detail: (personId: string) => ['resume-bank', 'detail', personId] as const,
}

/** RB-04 filters; empty values are left out of the query string. */
export interface ResumeBankFilters {
  q?: string
  skills?: string
  minExperience?: string
  maxExperience?: string
  location?: string
  maxCurrentCtc?: string
  ctcCurrency?: string
  maxNoticeDays?: string
  status?: ResumeBankStatus | ''
  contactedSince?: string
  tag?: string
  country?: string
  page?: number
  pageSize?: number
}

export function resumeBankUrl(filters: ResumeBankFilters = {}): string {
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(filters)) {
    const text = typeof value === 'number' ? String(value) : typeof value === 'string' ? value.trim() : ''
    if (text) params.append(key, text)
  }

  // The salary limit only means something in a currency.
  if (!params.get('maxCurrentCtc')) params.delete('ctcCurrency')
  const query = params.toString()
  return query ? `/api/resume-bank?${query}` : '/api/resume-bank'
}

export function useResumeBankSearch(filters: ResumeBankFilters) {
  const url = resumeBankUrl(filters)
  return useQuery({ queryKey: resumeBankKeys.search(url), queryFn: () => api.get<ResumeBankSearchResult>(url), placeholderData: (previous) => previous })
}

/** Salary figures in the response are audited views, so this is never kept around. */
export function useResumeBankCandidate(personId: string) {
  return useQuery({
    queryKey: resumeBankKeys.detail(personId),
    queryFn: () => api.get<ResumeBankDetail>(`/api/resume-bank/${encodeURIComponent(personId)}`),
    enabled: personId !== '',
    gcTime: 0,
  })
}

/** Every Resume Bank action returns the candidate's new detail. */
function useResumeBankAction<TBody>(path: string, send: (url: string, body: TBody) => Promise<ResumeBankDetail> = (url, body) => api.post(url, body)) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ personId, body }: { personId: string; body: TBody }) => send(`/api/resume-bank/${encodeURIComponent(personId)}${path}`, body),
    onSuccess: (detail) => {
      queryClient.setQueryData(resumeBankKeys.detail(detail.personId), detail)
      return queryClient.invalidateQueries({ queryKey: [...resumeBankKeys.all, 'search'] })
    },
  })
}

export const useUpdateResumeDetails = () =>
  useResumeBankAction<{ countryCode: string; location: string | null; experienceYears: number | null; skills: string[]; tags: string[] }>(
    '/details',
    (url, body) => api.put(url, body),
  )

export const useSetResumeBankStatus = () => useResumeBankAction<{ status: ResumeBankStatus | null; reason: string | null }>('/status')

export const useBlacklist = () => useResumeBankAction<{ reason: string }>('/blacklist')

export const useRemoveFromBlacklist = () => useResumeBankAction<{ reason: string }>('/unblacklist')

export const useAddCandidateNote = () => useResumeBankAction<{ body: string }>('/notes')

export const useMarkContacted = () => useResumeBankAction<Record<string, never>>('/contacted')

function fileForm(file: File): FormData {
  const form = new FormData()
  form.append('file', file)
  return form
}

export const downloadCandidateImportTemplate = () => api.download('/api/resume-bank/import/template')

export function useImportCandidates() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (file: File) => api.postForm<ImportResult>('/api/resume-bank/import', fileForm(file)),
    onSuccess: (result) => {
      if (result.imported > 0) void queryClient.invalidateQueries({ queryKey: resumeBankKeys.all })
    },
  })
}

export const downloadCandidateImportErrorReport = (file: File) => api.download('/api/resume-bank/import/error-report', fileForm(file))

/** Splits "Python; SQL, spark" into distinct lower-case entries, as the API stores them. */
export function parseList(text: string): string[] {
  return [
    ...new Set(
      text
        .split(/[,;]/)
        .map((s) => s.trim().toLowerCase())
        .filter((s) => s.length > 0),
    ),
  ]
}

export const statusLabels: Record<ResumeBankStatus, string> = {
  Shortlisted: 'Shortlisted',
  NotShortlisted: 'Not shortlisted',
  NoShow: 'No-show',
  OfferDeclined: 'Offer declined',
  Resigned: 'Resigned',
  Terminated: 'Terminated',
  Absconded: 'Absconded',
  Hired: 'Hired',
  Blacklisted: 'Blacklisted',
}
