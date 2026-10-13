import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './client'

export const templateCategories = [
  'Interview',
  'Rejection',
  'Offer',
  'Onboarding',
  'Probation',
  'Separation',
  'Termination',
  'Requisition',
  'Performance',
  'General',
] as const
export type TemplateCategory = (typeof templateCategories)[number]

export const templateRecordTypes = ['Application', 'Employee', 'Requisition'] as const
export type TemplateRecordType = (typeof templateRecordTypes)[number]
export type SenderMailbox = 'HrMailbox' | 'CurrentUser'

export const recordTypeLabels: Record<TemplateRecordType, string> = {
  Application: 'Candidate (application)',
  Employee: 'Employee',
  Requisition: 'Requisition approver',
}

export const senderLabels: Record<SenderMailbox, string> = {
  HrMailbox: 'Shared HR mailbox',
  CurrentUser: 'My mailbox',
}

export interface TemplateSummary {
  id: string
  name: string
  category: TemplateCategory
  recordType: TemplateRecordType
  countryCode: string | null
  isActive: boolean
  isRestricted: boolean
  defaultSender: SenderMailbox
  ccManager: boolean
  trigger: string | null
  version: number
  updatedAtUtc: string
}

export interface TemplateDetail extends TemplateSummary {
  subject: string
  bodyHtml: string
  versions: { version: number; changedByName: string; atUtc: string }[]
  canManage: boolean
}

export interface TemplateVersion {
  version: number
  name: string
  category: TemplateCategory
  subject: string
  bodyHtml: string
  countryCode: string | null
  defaultSender: SenderMailbox
  ccManager: boolean
  isActive: boolean
  changedByName: string
  atUtc: string
}

export interface TemplateRequest {
  name: string
  category: TemplateCategory
  recordType: TemplateRecordType
  subject: string
  bodyHtml: string
  countryCode: string | null
  defaultSender: SenderMailbox
  ccManager: boolean
  isActive: boolean
}

export interface MergeField {
  key: string
  label: string
  description: string
  recordTypes: TemplateRecordType[]
}

export interface EmailPreview {
  templateId: string
  recordId: string
  recipientName: string
  to: string | null
  cc: string[]
  subject: string
  bodyHtml: string
  missingFields: MergeField[]
  problems: string[]
  canSend: boolean
  defaultSender: SenderMailbox
}

export interface EmailLog {
  id: string
  recordType: TemplateRecordType
  recordId: string
  templateName: string | null
  templateVersion: number | null
  category: TemplateCategory | null
  to: string
  cc: string[]
  sender: SenderMailbox
  fromAddress: string
  subject: string
  bodyHtml: string
  attachments: string[]
  batchId: string | null
  sentByName: string
  sentAtUtc: string
  status: 'Sent' | 'Failed'
  error: string | null
}

export interface BulkItem {
  recordId: string
  name: string
  to: string | null
  emailId: string | null
  error: string | null
}

export interface BulkResult {
  batchId: string
  sent: BulkItem[]
  failed: BulkItem[]
}

export interface Attachment {
  id: string
  fileName: string
  sizeBytes: number
}

export const templateKeys = {
  all: ['email-templates'] as const,
  list: (query: string) => ['email-templates', 'list', query] as const,
  detail: (id: string) => ['email-templates', 'detail', id] as const,
  version: (id: string, version: number) => ['email-templates', 'detail', id, 'version', version] as const,
  mergeFields: (recordType: string) => ['email-templates', 'merge-fields', recordType] as const,
  history: (personId: string) => ['emails', personId] as const,
}

export interface TemplateFilters {
  recordType?: TemplateRecordType
  country?: string
  activeOnly?: boolean
}

export function templatesUrl(filters: TemplateFilters = {}): string {
  const params = new URLSearchParams()
  if (filters.recordType) params.append('recordType', filters.recordType)
  if (filters.country) params.append('country', filters.country)
  if (filters.activeOnly) params.append('activeOnly', 'true')
  const query = params.toString()
  return query ? `/api/email-templates?${query}` : '/api/email-templates'
}

export function useTemplates(filters: TemplateFilters = {}, enabled = true) {
  const url = templatesUrl(filters)
  return useQuery({ queryKey: templateKeys.list(url), queryFn: () => api.get<TemplateSummary[]>(url), enabled })
}

export function useTemplate(id: string) {
  return useQuery({
    queryKey: templateKeys.detail(id),
    queryFn: () => api.get<TemplateDetail>(`/api/email-templates/${encodeURIComponent(id)}`),
    enabled: id !== '',
  })
}

export function useTemplateVersion(id: string, version: number | null) {
  return useQuery({
    queryKey: templateKeys.version(id, version ?? 0),
    queryFn: () => api.get<TemplateVersion>(`/api/email-templates/${encodeURIComponent(id)}/versions/${version}`),
    enabled: id !== '' && version !== null,
  })
}

export function useMergeFields(recordType: TemplateRecordType) {
  return useQuery({
    queryKey: templateKeys.mergeFields(recordType),
    queryFn: () => api.get<MergeField[]>(`/api/email-templates/merge-fields?recordType=${recordType}`),
    staleTime: Infinity,
  })
}

export function useSaveTemplate(id: string | null) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: TemplateRequest) =>
      id ? api.put<TemplateDetail>(`/api/email-templates/${encodeURIComponent(id)}`, body) : api.post<TemplateDetail>('/api/email-templates', body),
    onSuccess: (detail) => {
      queryClient.setQueryData(templateKeys.detail(detail.id), detail)
      return queryClient.invalidateQueries({ queryKey: [...templateKeys.all, 'list'] })
    },
  })
}

export interface ComposeBody {
  templateId: string
  recordId: string
  subject: string | null
  bodyHtml: string | null
}

export const previewEmail = (body: ComposeBody) => api.post<EmailPreview>('/api/emails/preview', body)

export interface SendBody extends ComposeBody {
  sender: SenderMailbox
  attachmentIds: string[]
}

export const useSendTest = () => useMutation({ mutationFn: (body: SendBody) => api.post<{ sentTo: string }>('/api/emails/test', body) })

export function useSendEmail(personId?: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: SendBody) => api.post<EmailLog>('/api/emails/send', body),
    onSuccess: () => (personId ? queryClient.invalidateQueries({ queryKey: templateKeys.history(personId) }) : undefined),
  })
}

export function useSendBulk() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: { templateId: string; recordIds: string[]; sender: SenderMailbox; attachmentIds: string[] }) =>
      api.post<BulkResult>('/api/emails/bulk', body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['emails'] }),
  })
}

export const uploadAttachment = (file: File) => {
  const form = new FormData()
  form.append('file', file)
  return api.postForm<Attachment>('/api/emails/attachments', form)
}

/** ET-08: emails sent to a person, looked up by their person id or (on an employee record) the employee id. */
export function useEmailHistory(ref: { personId: string } | { employeeId: string }) {
  const url = 'personId' in ref ? `/api/people/${encodeURIComponent(ref.personId)}/emails` : `/api/employees/${encodeURIComponent(ref.employeeId)}/emails`
  const key = 'personId' in ref ? ref.personId : ref.employeeId
  return useQuery({ queryKey: templateKeys.history(key), queryFn: () => api.get<EmailLog[]>(url), enabled: key !== '' })
}

/** The merge fields written in a text, in order of first use (mirrors MergeFields.UsedIn on the server). */
export function fieldsIn(text: string): string[] {
  return [...new Set([...text.matchAll(/\{\{\s*([A-Za-z0-9_]+)\s*\}\}/g)].map((m) => m[1].toLowerCase()))]
}

/** Fields in the text that the record type cannot fill; the server refuses to save templates with these. */
export function unknownFields(text: string, available: readonly MergeField[]): string[] {
  const known = new Set(available.map((f) => f.key))
  return fieldsIn(text).filter((f) => !known.has(f))
}
