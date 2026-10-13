import { type QueryClient, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './client'
import type {
  AddCompensationRequest,
  CompensationRecord,
  CreateEmployeeRequest,
  DocumentCategory,
  EmployeeDetail,
  EmployeeDocument,
  EmployeeFilters,
  EmployeeSummary,
  HistoryEntry,
  ImportResult,
  JobChangeRequest,
  PersonalDetails,
  StatusChangeRequest,
  UpdateScheduledChangeRequest,
} from './types'

export const employeeKeys = {
  lists: ['employees', 'list'] as const,
  /** Keyed by URL, so equivalent filters (e.g. empty strings vs. missing) share one cached request. */
  list: (filters: EmployeeFilters) => ['employees', 'list', employeesUrl(filters)] as const,
  detail: (id: string) => ['employees', id] as const,
  personal: (id: string) => ['employees', id, 'personal'] as const,
  history: (id: string) => ['employees', id, 'history'] as const,
  compensation: (id: string) => ['employees', id, 'compensation'] as const,
  documents: (id: string) => ['employees', id, 'documents'] as const,
}

/** `/api/employees?...` for a set of directory filters; empty filters are left out. */
export function employeesUrl(filters: EmployeeFilters = {}): string {
  const params = new URLSearchParams()
  const { status, ...single } = filters
  for (const [key, value] of Object.entries(single)) {
    const text = value?.trim()
    if (text) params.append(key, text)
  }
  for (const s of status ?? []) params.append('status', s)
  const query = params.toString()
  return query ? `/api/employees?${query}` : '/api/employees'
}

export function useEmployees(filters: EmployeeFilters = {}) {
  return useQuery({ queryKey: employeeKeys.list(filters), queryFn: () => api.get<EmployeeSummary[]>(employeesUrl(filters)) })
}

export function useEmployee(id: string) {
  return useQuery({ queryKey: employeeKeys.detail(id), queryFn: () => api.get<EmployeeDetail>(`/api/employees/${encodeURIComponent(id)}`) })
}

export function useEmployeeHistory(id: string) {
  return useQuery({ queryKey: employeeKeys.history(id), queryFn: () => api.get<HistoryEntry[]>(`/api/employees/${encodeURIComponent(id)}/history`) })
}

/** Each fetch is audited as a view of salary data, so only enable it when the salary is actually shown. */
export function useCompensation(id: string, enabled: boolean) {
  return useQuery({
    queryKey: employeeKeys.compensation(id),
    queryFn: () => api.get<CompensationRecord[]>(`/api/employees/${encodeURIComponent(id)}/compensation`),
    enabled,
    staleTime: 0,
    gcTime: 0,
  })
}

export function useDocuments(id: string, enabled: boolean) {
  return useQuery({
    queryKey: employeeKeys.documents(id),
    queryFn: () => api.get<EmployeeDocument[]>(`/api/employees/${encodeURIComponent(id)}/documents`),
    enabled,
  })
}

function useEmployeeMutation<TBody, TResult = EmployeeDetail>(path: (id: string) => string, send: (url: string, body: TBody) => Promise<TResult>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: TBody }) => send(path(id), body),
    onSuccess: (_, { id }) => refreshRecord(queryClient, id),
  })
}

export function useCreateEmployee() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: CreateEmployeeRequest) => api.post<EmployeeDetail>('/api/employees', body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: employeeKeys.lists }),
  })
}

const base = (id: string) => `/api/employees/${encodeURIComponent(id)}`

/**
 * Personal details, fetched only when shown: a manager's view of a team member's details is recorded in the audit
 * log, so it is never prefetched or cached.
 */
export function usePersonal(id: string, enabled: boolean) {
  return useQuery({
    queryKey: employeeKeys.personal(id),
    queryFn: () => api.get<PersonalDetails>(`${base(id)}/personal`),
    enabled,
    staleTime: 0,
    gcTime: 0,
  })
}

export function useUpdatePersonal() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: { givenName: string; familyName: string; personal: PersonalDetails } }) =>
      api.put<PersonalDetails>(`${base(id)}/personal`, body),
    onSuccess: (personal, { id }) => {
      queryClient.setQueryData(employeeKeys.personal(id), personal)
      return queryClient.invalidateQueries({ queryKey: employeeKeys.detail(id), exact: true })
    },
  })
}

export function useUpdateScheduledChange() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, changeId, body }: { id: string; changeId: string; body: UpdateScheduledChangeRequest }) =>
      api.put<EmployeeDetail>(`${base(id)}/scheduled-changes/${encodeURIComponent(changeId)}`, body),
    onSuccess: (detail, { id }) => queryClient.setQueryData(employeeKeys.detail(id), detail),
  })
}

export function useCancelScheduledChange() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, changeId }: { id: string; changeId: string }) =>
      api.delete(`${base(id)}/scheduled-changes/${encodeURIComponent(changeId)}`),
    onSuccess: (_, { id }) => refreshRecord(queryClient, id),
  })
}

/**
 * Refreshes the record and its history after a change. Exact keys only: a prefix match would also re-fetch an open
 * personal-details or compensation tab, and each of those fetches can be an audited view.
 */
function refreshRecord(queryClient: QueryClient, id: string) {
  return Promise.all([
    queryClient.invalidateQueries({ queryKey: employeeKeys.detail(id), exact: true }),
    queryClient.invalidateQueries({ queryKey: employeeKeys.history(id), exact: true }),
  ])
}

export const useChangeJob = () =>
  useEmployeeMutation((id) => `${base(id)}/job-changes`, (url, body: JobChangeRequest) => api.post<EmployeeDetail>(url, body))

export const useChangeManager = () =>
  useEmployeeMutation((id) => `${base(id)}/manager`, (url, body: { managerId: string | null; effectiveDate: string; reason: string | null }) =>
    api.post<EmployeeDetail>(url, body),
  )

export const useChangeStatus = () =>
  useEmployeeMutation((id) => `${base(id)}/status`, (url, body: StatusChangeRequest) => api.post<EmployeeDetail>(url, body))

export function useAddCompensation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: AddCompensationRequest }) =>
      api.post<CompensationRecord[]>(`${base(id)}/compensation`, body),
    onSuccess: (records, { id }) => queryClient.setQueryData(employeeKeys.compensation(id), records),
  })
}

export function useUploadDocument() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, file, category, expiryDate }: { id: string; file: File; category: DocumentCategory; expiryDate: string | null }) => {
      const form = new FormData()
      form.append('file', file)
      form.append('category', category)
      if (expiryDate) form.append('expiryDate', expiryDate)
      return api.postForm<EmployeeDocument>(`${base(id)}/documents`, form)
    },
    onSuccess: (_, { id }) => queryClient.invalidateQueries({ queryKey: employeeKeys.documents(id) }),
  })
}

export function useDeleteDocument() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, documentId }: { id: string; documentId: string }) => api.delete(`${base(id)}/documents/${encodeURIComponent(documentId)}`),
    onSuccess: (_, { id }) => queryClient.invalidateQueries({ queryKey: employeeKeys.documents(id) }),
  })
}

export const downloadDocument = (id: string, documentId: string) =>
  api.download(`${base(id)}/documents/${encodeURIComponent(documentId)}/content`)

export const downloadImportTemplate = () => api.download('/api/employees/import/template')

function fileForm(file: File): FormData {
  const form = new FormData()
  form.append('file', file)
  return form
}

export function useImportEmployees() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (file: File) => api.postForm<ImportResult>('/api/employees/import', fileForm(file)),
    onSuccess: (result) => {
      if (result.imported > 0) void queryClient.invalidateQueries({ queryKey: employeeKeys.lists })
    },
  })
}

/** Re-validates the same file on the server and returns it annotated with the errors (nothing is stored between calls). */
export const downloadImportErrorReport = (file: File) => api.download('/api/employees/import/error-report', fileForm(file))
