export interface Me {
  userId: string
  displayName: string
  roles: string[]
  /** The signed-in user's employee record, if their account is linked to one. */
  employeeId: string | null
  countryCode: string | null
  /** Countries the user sees as HR staff (MC-03). Empty for non-HR users. */
  hrCountries: { all: boolean; codes: string[] }
  /** In a requisition approval chain: shows the Approvals page. */
  isApprover: boolean
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
  defaultTimeZone: string
  probationMonths: number
  noticePeriodDays: number
}

export interface UpdateCountrySettingsRequest {
  name: string
  defaultTimeZone: string
  probationMonths: number
  noticePeriodDays: number
}

export interface CreateCountryRequest extends UpdateCountrySettingsRequest {
  countryCode: string
  currencyCode: string
}

export interface LegalEntity {
  id: string
  countryCode: string
  name: string
  isActive: boolean
}

export interface OfficeLocation {
  id: string
  countryCode: string
  name: string
  city: string | null
  /** Override; null means the country default applies. */
  timeZone: string | null
  effectiveTimeZone: string
  isActive: boolean
}

export interface Department {
  id: string
  code: string
  name: string
  isActive: boolean
}

export interface Grade {
  id: string
  countryCode: string
  code: string
  name: string
  level: number
  probationMonths: number | null
  noticePeriodDays: number | null
  effectiveProbationMonths: number
  effectiveNoticePeriodDays: number
  isActive: boolean
}

export const employmentStatuses = ['Probation', 'Confirmed', 'OnNotice', 'Resigned', 'Terminated', 'Absconded'] as const
export type EmploymentStatus = (typeof employmentStatuses)[number]

export const employmentTypes = ['Permanent', 'Contract', 'Intern'] as const
export type EmploymentType = (typeof employmentTypes)[number]

export type JobChangeKind = 'Joined' | 'Promotion' | 'Transfer' | 'ManagerChange' | 'Redesignation' | 'Correction' | 'StatusChange'

export const compensationReasons = ['Joining', 'Increment', 'Promotion', 'Adjustment', 'Correction'] as const
export type CompensationReason = (typeof compensationReasons)[number]

export const documentCategories = ['IdProof', 'Certificate', 'Contract', 'SignedLetter', 'Other'] as const
export type DocumentCategory = (typeof documentCategories)[number]

export interface EmployeeSummary {
  id: string
  employeeNumber: string
  fullName: string
  designation: string
  workEmail: string
  countryCode: string
  departmentId: string
  departmentName: string
  officeLocationId: string
  officeLocationName: string
  managerId: string | null
  managerName: string | null
  status: EmploymentStatus
  employmentType: EmploymentType
}

export interface EmployeeFilters {
  search?: string
  departmentId?: string
  officeLocationId?: string
  managerId?: string
  countryCode?: string
  status?: EmploymentStatus[]
}

export interface EmergencyContact {
  name: string
  relationship: string
  phone: string
}

export interface PersonalDetails {
  personalEmail: string | null
  phone: string | null
  dateOfBirth: string | null
  addressLine1: string | null
  addressLine2: string | null
  city: string | null
  region: string | null
  postalCode: string | null
  addressCountryCode: string | null
  nationalId: string | null
  emergencyContacts: EmergencyContact[]
}

/** Computed by the API for the signed-in user; the UI only hides controls, the API enforces. */
export interface EmployeePermissions {
  canEdit: boolean
  /** Contact details, address, date of birth, emergency contacts. Includes managers for their team. */
  canViewPersonal: boolean
  /** National ID (CNIC/SSN): HR and the employee only. */
  canViewNationalId: boolean
  canEditPersonal: boolean
  canViewCompensation: boolean
  canEditCompensation: boolean
  /** Documents: HR and the employee only, never managers. */
  canViewDocuments: boolean
  canManageDocuments: boolean
  canTerminate: boolean
}

export type ScheduledChangeStatus = 'Pending' | 'Applied' | 'Cancelled' | 'Failed'

/** A future-dated job or manager change; Pending shows as "Upcoming". */
export interface ScheduledChange {
  id: string
  kind: JobChangeKind
  effectiveDate: string
  reason: string | null
  status: ScheduledChangeStatus
  outcome: string | null
  designation: string | null
  legalEntityId: string | null
  legalEntityName: string | null
  officeLocationId: string | null
  officeLocationName: string | null
  departmentId: string | null
  departmentName: string | null
  gradeId: string | null
  gradeName: string | null
  newManagerId: string | null
  newManagerName: string | null
}

export interface UpdateScheduledChangeRequest {
  effectiveDate: string
  reason: string | null
  designation: string | null
  legalEntityId: string | null
  officeLocationId: string | null
  departmentId: string | null
  gradeId: string | null
  managerId: string | null
}

export interface EmployeeDetail {
  id: string
  employeeNumber: string
  givenName: string
  familyName: string
  designation: string
  workEmail: string
  countryCode: string
  legalEntityId: string
  legalEntityName: string
  officeLocationId: string
  officeLocationName: string
  departmentId: string
  departmentName: string
  gradeId: string | null
  gradeName: string | null
  managerId: string | null
  managerName: string | null
  employmentType: EmploymentType
  status: EmploymentStatus
  nextStatuses: EmploymentStatus[]
  joiningDate: string
  probationEndDate: string | null
  lastWorkingDate: string | null
  /** Pending and failed scheduled changes; only returned to users who can edit the record. */
  upcoming: ScheduledChange[]
  permissions: EmployeePermissions
}

export interface CreateEmployeeRequest {
  givenName: string
  familyName: string
  employeeNumber: string
  workEmail: string
  employmentType: EmploymentType
  designation: string
  legalEntityId: string
  officeLocationId: string
  departmentId: string
  gradeId: string | null
  managerId: string | null
  joiningDate: string
  personal: PersonalDetails | null
}

export interface JobChangeRequest {
  kind: JobChangeKind
  effectiveDate: string
  designation: string
  legalEntityId: string
  officeLocationId: string
  departmentId: string
  gradeId: string | null
  reason: string | null
}

export interface StatusChangeRequest {
  status: EmploymentStatus
  effectiveDate: string
  lastWorkingDate: string | null
  reason: string | null
}

export interface HistoryEntry {
  id: string
  kind: JobChangeKind
  effectiveDate: string
  reason: string | null
  designation: string
  departmentName: string
  officeLocationName: string
  legalEntityName: string
  gradeName: string | null
  managerName: string | null
  status: EmploymentStatus
}

export interface CompensationRecord {
  id: string
  effectiveDate: string
  annualCtc: number
  currencyCode: string
  salaryBand: string | null
  reason: CompensationReason
  note: string | null
  isCurrent: boolean
}

export interface AddCompensationRequest {
  effectiveDate: string
  annualCtc: number
  currencyCode: string
  salaryBand: string | null
  reason: CompensationReason
  note: string | null
}

export interface EmployeeDocument {
  id: string
  category: DocumentCategory
  fileName: string
  contentType: string
  sizeBytes: number
  expiryDate: string | null
  uploadedAtUtc: string
}

export interface ImportError {
  row: number
  column: string
  message: string
}

export interface ImportResult {
  totalRows: number
  imported: number
  errors: ImportError[]
}

export interface HrAccess {
  employeeId: string
  employeeNumber: string
  employeeName: string
  employeeCountryCode: string
  allCountries: boolean
  countryCodes: string[]
  compensationAccess: boolean
}

export interface UpdateHrAccessRequest {
  allCountries: boolean
  countryCodes: string[]
  compensationAccess: boolean
}

/** RFC 9457 problem details as returned by the API. */
export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  errors?: Record<string, string[]>
}

export const requisitionStatuses = ['Draft', 'PendingApproval', 'Open', 'OnHold', 'Filled', 'Cancelled'] as const
export type RequisitionStatus = (typeof requisitionStatuses)[number]
export type RequisitionReason = 'NewPosition' | 'Replacement'
export type ApprovalDecision = 'Pending' | 'Approved' | 'Rejected' | 'Closed'

export interface RequisitionSummary {
  id: string
  number: string
  title: string
  departmentName: string
  officeLocationName: string
  countryCode: string
  openings: number
  budgetMin: number
  budgetMax: number
  currencyCode: string
  status: RequisitionStatus
  targetDate: string
  requesterName: string
  createdAtUtc: string
}

export interface RequisitionApproval {
  round: number
  stepOrder: number
  label: string
  approverEmployeeId: string
  approverName: string
  decision: ApprovalDecision
  comment: string | null
  decidedAtUtc: string | null
}

export interface RequisitionStatusChange {
  fromStatus: RequisitionStatus | null
  toStatus: RequisitionStatus
  actorName: string
  atUtc: string
  comment: string | null
}

/** Computed by the API for the signed-in user. */
export interface RequisitionPermissions {
  canEdit: boolean
  canSubmit: boolean
  canWithdraw: boolean
  canDecide: boolean
  statusOptions: RequisitionStatus[]
}

export interface RequisitionDetail extends RequisitionSummary {
  departmentId: string
  officeLocationId: string
  reason: RequisitionReason
  replacedEmployeeId: string | null
  replacedEmployeeName: string | null
  jobDescription: string
  requesterEmployeeId: string
  approvals: RequisitionApproval[]
  history: RequisitionStatusChange[]
  permissions: RequisitionPermissions
}

/** The budget is in the office's country currency (MC-02); there is no currency to choose. */
export interface RequisitionRequest {
  title: string
  departmentId: string
  officeLocationId: string
  openings: number
  budgetMin: number
  budgetMax: number
  reason: RequisitionReason
  replacedEmployeeId: string | null
  targetDate: string
  jobDescription: string
}

export interface ApprovalChain {
  countryCode: string
  countryName: string
  steps: { label: string; approverEmployeeId: string; approverName: string; approverActive: boolean }[]
}

export interface AppNotification {
  id: string
  title: string
  body: string
  link: string
  createdAtUtc: string
  isRead: boolean
}

export interface NotificationList {
  unreadCount: number
  items: AppNotification[]
}
