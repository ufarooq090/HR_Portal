import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './client'
import type {
  ApprovalChain,
  NotificationList,
  RequisitionDetail,
  RequisitionRequest,
  RequisitionStatus,
  RequisitionSummary,
} from './types'

export const requisitionKeys = {
  lists: ['requisitions', 'list'] as const,
  list: (status: RequisitionStatus[]) => ['requisitions', 'list', status.join(',')] as const,
  detail: (id: string) => ['requisitions', id] as const,
  approvals: ['approvals'] as const,
  chains: ['approval-chains'] as const,
  notifications: ['notifications'] as const,
}

const base = (id: string) => `/api/requisitions/${encodeURIComponent(id)}`

export function requisitionsUrl(status: RequisitionStatus[] = []): string {
  const params = new URLSearchParams()
  for (const s of status) params.append('status', s)
  const query = params.toString()
  return query ? `/api/requisitions?${query}` : '/api/requisitions'
}

export function useRequisitions(status: RequisitionStatus[] = []) {
  return useQuery({ queryKey: requisitionKeys.list(status), queryFn: () => api.get<RequisitionSummary[]>(requisitionsUrl(status)) })
}

export function useRequisition(id: string) {
  return useQuery({ queryKey: requisitionKeys.detail(id), queryFn: () => api.get<RequisitionDetail>(base(id)), enabled: id !== '' })
}

/** Requisitions waiting for the signed-in user's decision. */
export function useApprovals(enabled = true) {
  return useQuery({ queryKey: requisitionKeys.approvals, queryFn: () => api.get<RequisitionSummary[]>('/api/approvals'), enabled })
}

export function useCreateRequisition() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: RequisitionRequest) => api.post<RequisitionDetail>('/api/requisitions', body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: requisitionKeys.lists }),
  })
}

/** Any action that returns the updated requisition: store it and refresh lists, approvals and notifications. */
function useRequisitionAction<TBody>(path: string, send: (url: string, body: TBody) => Promise<RequisitionDetail>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: TBody }) => send(`${base(id)}${path}`, body),
    onSuccess: (detail) => {
      queryClient.setQueryData(requisitionKeys.detail(detail.id), detail)
      return Promise.all([
        queryClient.invalidateQueries({ queryKey: requisitionKeys.lists }),
        queryClient.invalidateQueries({ queryKey: requisitionKeys.approvals }),
        queryClient.invalidateQueries({ queryKey: requisitionKeys.notifications }),
      ])
    },
  })
}

export const useUpdateRequisition = () => useRequisitionAction('', (url, body: RequisitionRequest) => api.put<RequisitionDetail>(url, body))
export const useSubmitRequisition = () => useRequisitionAction('/submit', (url, body: object) => api.post<RequisitionDetail>(url, body))
export const useWithdrawRequisition = () =>
  useRequisitionAction('/withdraw', (url, body: { comment: string | null }) => api.post<RequisitionDetail>(url, body))
export const useDecideRequisition = () =>
  useRequisitionAction('/decision', (url, body: { approve: boolean; comment: string | null }) => api.post<RequisitionDetail>(url, body))
export const useChangeRequisitionStatus = () =>
  useRequisitionAction('/status', (url, body: { status: RequisitionStatus; comment: string | null }) => api.post<RequisitionDetail>(url, body))

export function useApprovalChains(enabled = true) {
  return useQuery({ queryKey: requisitionKeys.chains, queryFn: () => api.get<ApprovalChain[]>('/api/approval-chains'), enabled })
}

export function useSaveApprovalChain() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ countryCode, steps }: { countryCode: string; steps: { label: string; approverEmployeeId: string }[] }) =>
      api.put<ApprovalChain>(`/api/approval-chains/${encodeURIComponent(countryCode)}`, { steps }),
    onSuccess: () => Promise.all([queryClient.invalidateQueries({ queryKey: requisitionKeys.chains }), queryClient.invalidateQueries({ queryKey: ['me'] })]),
  })
}

/** Polled once a minute while the app is open, so new approval requests show up without a reload. */
export function useNotifications() {
  return useQuery({
    queryKey: requisitionKeys.notifications,
    queryFn: () => api.get<NotificationList>('/api/notifications'),
    refetchInterval: 60_000,
  })
}

export function useMarkNotificationRead() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string | 'all') =>
      id === 'all' ? api.post<void>('/api/notifications/read-all', {}) : api.post<void>(`/api/notifications/${encodeURIComponent(id)}/read`, {}),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: requisitionKeys.notifications }),
  })
}
