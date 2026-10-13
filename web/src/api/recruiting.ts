import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './client'
import type {
  ApplicationCard,
  ApplicationDetail,
  Board,
  CandidateDetail,
  CandidateRequest,
  CandidateSource,
  CriterionRating,
  Cv,
  DuplicateMatch,
  InterviewMode,
  MyInterview,
  PipelineStage,
  Recommendation,
  RejectionReason,
} from './types'

export const recruitingKeys = {
  boards: ['board'] as const,
  board: (requisitionId: string) => ['board', requisitionId] as const,
  list: (url: string) => ['candidates', url] as const,
  candidate: (personId: string) => ['candidate', personId] as const,
  application: (id: string) => ['application', id] as const,
  myInterviews: ['interviews', 'mine'] as const,
  rejectionReasons: ['rejection-reasons'] as const,
}

export interface CandidateFilters {
  search?: string
  requisitionId?: string
  stage?: PipelineStage
  source?: CandidateSource
}

export function candidatesUrl(filters: CandidateFilters = {}): string {
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(filters)) {
    const text = typeof value === 'string' ? value.trim() : ''
    if (text) params.append(key, text)
  }
  const query = params.toString()
  return query ? `/api/candidates?${query}` : '/api/candidates'
}

export function useCandidates(filters: CandidateFilters = {}) {
  const url = candidatesUrl(filters)
  return useQuery({ queryKey: recruitingKeys.list(url), queryFn: () => api.get<ApplicationCard[]>(url) })
}

export function useBoard(requisitionId: string) {
  return useQuery({
    queryKey: recruitingKeys.board(requisitionId),
    queryFn: () => api.get<Board>(`/api/requisitions/${encodeURIComponent(requisitionId)}/applications`),
    enabled: requisitionId !== '',
  })
}

/** Salary figures in the response are audited views, so this is never prefetched or kept around. */
export function useCandidate(personId: string) {
  return useQuery({
    queryKey: recruitingKeys.candidate(personId),
    queryFn: () => api.get<CandidateDetail>(`/api/candidates/${encodeURIComponent(personId)}`),
    enabled: personId !== '',
    gcTime: 0,
  })
}

export function useApplication(id: string) {
  return useQuery({
    queryKey: recruitingKeys.application(id),
    queryFn: () => api.get<ApplicationDetail>(`/api/applications/${encodeURIComponent(id)}`),
    enabled: id !== '',
    gcTime: 0,
  })
}

export function useMyInterviews() {
  return useQuery({ queryKey: recruitingKeys.myInterviews, queryFn: () => api.get<MyInterview[]>('/api/interviews/mine') })
}

export function useRejectionReasons(enabled = true) {
  return useQuery({
    queryKey: recruitingKeys.rejectionReasons,
    queryFn: () => api.get<RejectionReason[]>('/api/recruiting/rejection-reasons'),
    enabled,
  })
}

export const checkDuplicates = (email: string | null, phone: string | null) =>
  api.post<DuplicateMatch[]>('/api/candidates/duplicates', { email, phone })

export function useAddApplication(requisitionId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: { personId: string | null; candidate: CandidateRequest | null; source: CandidateSource }) =>
      api.post<ApplicationCard>(`/api/requisitions/${encodeURIComponent(requisitionId)}/applications`, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: recruitingKeys.board(requisitionId) }),
  })
}

/** Actions on an application return its new detail; refresh the board and lists around it. */
function useApplicationAction<TBody>(path: string, send: (url: string, body: TBody) => Promise<ApplicationDetail>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: TBody }) => send(`/api/applications/${encodeURIComponent(id)}${path}`, body),
    onSuccess: (detail) => {
      queryClient.setQueryData(recruitingKeys.application(detail.id), detail)
      return Promise.all([
        queryClient.invalidateQueries({ queryKey: recruitingKeys.boards }),
        queryClient.invalidateQueries({ queryKey: ['candidates'] }),
        queryClient.invalidateQueries({ queryKey: recruitingKeys.myInterviews }),
      ])
    },
  })
}

export const useMoveStage = () =>
  useApplicationAction(
    '/stage',
    (url, body: { stage: PipelineStage; round: number | null; rejectionReasonId: string | null; reason: string | null }) =>
      api.post<ApplicationDetail>(url, body),
  )

export const useAddNote = () => useApplicationAction('/notes', (url, body: { body: string }) => api.post<ApplicationDetail>(url, body))

export const useScheduleInterview = () =>
  useApplicationAction(
    '/interviews',
    (
      url,
      body: { round: number; startUtc: string; durationMinutes: number; mode: InterviewMode; location: string | null; panelEmployeeIds: string[] },
    ) => api.post<ApplicationDetail>(url, body),
  )

export function useCancelInterview() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, interviewId }: { id: string; interviewId: string }) =>
      api.delete(`/api/applications/${encodeURIComponent(id)}/interviews/${encodeURIComponent(interviewId)}`),
    onSuccess: (_, { id }) => queryClient.invalidateQueries({ queryKey: recruitingKeys.application(id) }),
  })
}

export function useSubmitScorecard() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ interviewId, ...body }: { interviewId: string; ratings: CriterionRating[]; recommendation: Recommendation; comment: string | null }) =>
      api.post<ApplicationDetail>(`/api/interviews/${encodeURIComponent(interviewId)}/scorecard`, body),
    onSuccess: (detail) => {
      queryClient.setQueryData(recruitingKeys.application(detail.id), detail)
      return queryClient.invalidateQueries({ queryKey: recruitingKeys.myInterviews })
    },
  })
}

export function useUpdateCandidate() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ personId, body }: { personId: string; body: CandidateRequest }) =>
      api.put<CandidateDetail>(`/api/candidates/${encodeURIComponent(personId)}`, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['application'] }),
  })
}

export function useUploadCv() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ personId, file }: { personId: string; file: File }) => {
      const form = new FormData()
      form.append('file', file)
      return api.postForm<Cv>(`/api/candidates/${encodeURIComponent(personId)}/cvs`, form)
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['application'] }),
  })
}

export const downloadCv = (personId: string, cvId: string) =>
  api.download(`/api/candidates/${encodeURIComponent(personId)}/cvs/${encodeURIComponent(cvId)}/content`)

export function useSavePipeline(requisitionId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: { interviewRounds: number; scorecardCriteria: string[] }) =>
      api.put<unknown>(`/api/requisitions/${encodeURIComponent(requisitionId)}/pipeline`, body),
    onSuccess: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: recruitingKeys.board(requisitionId) }),
        queryClient.invalidateQueries({ queryKey: ['requisitions', requisitionId] }),
      ]),
  })
}
