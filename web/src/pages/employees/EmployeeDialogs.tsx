import {
  Button,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  makeStyles,
  tokens,
} from '@fluentui/react-components'
import { type ReactNode, useState } from 'react'
import { ApiError } from '../../api/client'
import { useChangeJob, useChangeManager, useChangeStatus, useUpdatePersonal, useUpdateScheduledChange } from '../../api/employees'
import type { EmployeeDetail, EmploymentStatus, JobChangeKind, PersonalDetails, ScheduledChange } from '../../api/types'
import { QueryError } from '../../app/QueryError'
import { ManagerField, PersonalSection, type Placement, PlacementSection } from './EmployeeFormSections'
import { SelectField, TextField } from './fields'
import { label, todayIso } from './format'
import { cleanPersonal, emptyPersonal } from './personal'

const useStyles = makeStyles({
  form: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  wide: { maxWidth: '920px', width: '100%' },
})

/** Dialog shell with the API's general error (field errors are shown on the fields). */
function FormDialog({
  title,
  wide,
  error,
  pending,
  submitLabel = 'Save',
  onSubmit,
  onClose,
  children,
}: {
  title: string
  wide?: boolean
  error: unknown
  pending: boolean
  submitLabel?: string
  onSubmit: () => void
  onClose: () => void
  children: ReactNode
}) {
  const styles = useStyles()
  const apiError = error instanceof ApiError ? error : undefined
  return (
    <Dialog open onOpenChange={(_, d) => !d.open && onClose()}>
      <DialogSurface className={wide ? styles.wide : undefined}>
        <form
          onSubmit={(e) => {
            e.preventDefault()
            onSubmit()
          }}
        >
          <DialogBody>
            <DialogTitle>{title}</DialogTitle>
            <DialogContent className={styles.form}>
              {children}
              {error && !apiError?.problem?.errors ? <QueryError error={error} /> : null}
            </DialogContent>
            <DialogActions>
              <Button onClick={onClose}>Cancel</Button>
              <Button appearance="primary" type="submit" disabled={pending}>
                {pending ? 'Saving…' : submitLabel}
              </Button>
            </DialogActions>
          </DialogBody>
        </form>
      </DialogSurface>
    </Dialog>
  )
}

const fieldErrors = (error: unknown) => (field: string) => (error instanceof ApiError ? error.fieldError(field) : undefined)

/** EMP-05: promotion, transfer, redesignation or correction, recorded with an effective date. */
const futureHint = 'A future date schedules the change; it is applied automatically on that day.'

/**
 * EMP-05: promotion, transfer, redesignation or correction. A future effective date schedules it (shown as
 * Upcoming); passing <paramref name="scheduled"/> edits a pending change instead.
 */
export function JobChangeDialog({ employee, scheduled, onClose }: { employee: EmployeeDetail; scheduled?: ScheduledChange; onClose: () => void }) {
  const change = useChangeJob()
  const update = useUpdateScheduledChange()
  const mutation = scheduled ? update : change
  const [kind, setKind] = useState<JobChangeKind>(scheduled?.kind ?? 'Promotion')
  const [effectiveDate, setEffectiveDate] = useState(scheduled?.effectiveDate ?? todayIso())
  const [reason, setReason] = useState(scheduled?.reason ?? '')
  const [placement, setPlacement] = useState<Placement>({
    designation: scheduled?.designation ?? employee.designation,
    legalEntityId: scheduled?.legalEntityId ?? employee.legalEntityId,
    officeLocationId: scheduled?.officeLocationId ?? employee.officeLocationId,
    departmentId: scheduled?.departmentId ?? employee.departmentId,
    gradeId: (scheduled ? scheduled.gradeId : employee.gradeId) ?? '',
  })
  const error = fieldErrors(mutation.error)

  const submit = () => {
    const fields = { ...placement, gradeId: placement.gradeId || null, reason: reason || null, effectiveDate }
    if (scheduled) {
      update.mutate({ id: employee.id, changeId: scheduled.id, body: { ...fields, managerId: null } }, { onSuccess: onClose })
    } else {
      change.mutate({ id: employee.id, body: { kind, ...fields } }, { onSuccess: onClose })
    }
  }

  return (
    <FormDialog
      title={scheduled ? `Edit upcoming ${label(scheduled.kind).toLowerCase()}` : 'Change job'}
      wide
      error={mutation.error}
      pending={mutation.isPending}
      onClose={onClose}
      onSubmit={submit}
    >
      {!scheduled && (
        <SelectField
          label="Type of change"
          required
          value={kind}
          onChange={(v) => setKind(v as JobChangeKind)}
          options={(['Promotion', 'Transfer', 'Redesignation', 'Correction'] as const).map((k) => ({ value: k, label: label(k) }))}
          error={error('kind')}
        />
      )}
      <TextField
        label="Effective date"
        type="date"
        required
        value={effectiveDate}
        onChange={setEffectiveDate}
        hint={futureHint}
        error={error('effectiveDate')}
      />
      <PlacementSection value={placement} onChange={setPlacement} error={error} />
      <TextField label="Reason" multiline value={reason} onChange={setReason} error={error('reason')} />
    </FormDialog>
  )
}

/** New reporting manager, now or on a future date. Passing <paramref name="scheduled"/> edits a pending manager change. */
export function ManagerDialog({ employee, scheduled, onClose }: { employee: EmployeeDetail; scheduled?: ScheduledChange; onClose: () => void }) {
  const change = useChangeManager()
  const update = useUpdateScheduledChange()
  const mutation = scheduled ? update : change
  const [managerId, setManagerId] = useState((scheduled ? scheduled.newManagerId : employee.managerId) ?? '')
  const [effectiveDate, setEffectiveDate] = useState(scheduled?.effectiveDate ?? todayIso())
  const [reason, setReason] = useState(scheduled?.reason ?? '')
  const error = fieldErrors(mutation.error)

  const submit = () => {
    const fields = { managerId: managerId || null, effectiveDate, reason: reason || null }
    if (scheduled) {
      update.mutate(
        {
          id: employee.id,
          changeId: scheduled.id,
          body: { ...fields, designation: null, legalEntityId: null, officeLocationId: null, departmentId: null, gradeId: null },
        },
        { onSuccess: onClose },
      )
    } else {
      change.mutate({ id: employee.id, body: fields }, { onSuccess: onClose })
    }
  }

  return (
    <FormDialog
      title={scheduled ? 'Edit upcoming manager change' : 'Change manager'}
      error={mutation.error}
      pending={mutation.isPending}
      onClose={onClose}
      onSubmit={submit}
    >
      <ManagerField value={managerId} onChange={setManagerId} excludeId={employee.id} error={error('managerId')} />
      <TextField
        label="Effective date"
        type="date"
        required
        value={effectiveDate}
        onChange={setEffectiveDate}
        hint={futureHint}
        error={error('effectiveDate')}
      />
      <TextField label="Reason" multiline value={reason} onChange={setReason} />
    </FormDialog>
  )
}

/** EMP-04. Only statuses the employee can move to are offered; termination only to HR Admins (the API enforces both). */
export function StatusDialog({ employee, onClose }: { employee: EmployeeDetail; onClose: () => void }) {
  const change = useChangeStatus()
  const options = employee.nextStatuses.filter((s) => s !== 'Terminated' || employee.permissions.canTerminate)
  const [status, setStatus] = useState<EmploymentStatus | ''>(options[0] ?? '')
  const [effectiveDate, setEffectiveDate] = useState(todayIso())
  const [lastWorkingDate, setLastWorkingDate] = useState('')
  const [reason, setReason] = useState('')
  const error = fieldErrors(change.error)

  return (
    <FormDialog
      title="Change status"
      error={change.error}
      pending={change.isPending}
      onClose={onClose}
      onSubmit={() =>
        status &&
        change.mutate(
          { id: employee.id, body: { status, effectiveDate, lastWorkingDate: lastWorkingDate || null, reason: reason || null } },
          { onSuccess: onClose },
        )
      }
    >
      <SelectField
        label="New status"
        required
        value={status}
        onChange={(v) => setStatus(v as EmploymentStatus)}
        options={options.map((s) => ({ value: s, label: label(s) }))}
        error={error('status')}
      />
      <TextField label="Effective date" type="date" required value={effectiveDate} onChange={setEffectiveDate} error={error('effectiveDate')} />
      {status && status !== 'Confirmed' && (
        <TextField
          label="Last working day"
          type="date"
          value={lastWorkingDate}
          onChange={setLastWorkingDate}
          hint={status === 'OnNotice' ? 'Leave empty to use the notice period.' : 'Leave empty to use the effective date.'}
          error={error('lastWorkingDate')}
        />
      )}
      <TextField label="Reason" multiline value={reason} onChange={setReason} />
    </FormDialog>
  )
}

export function PersonalDialog({
  employee,
  personal: current,
  onClose,
}: {
  employee: EmployeeDetail
  personal: PersonalDetails | undefined
  onClose: () => void
}) {
  const update = useUpdatePersonal()
  const [givenName, setGivenName] = useState(employee.givenName)
  const [familyName, setFamilyName] = useState(employee.familyName)
  const [personal, setPersonal] = useState<PersonalDetails>(current ?? emptyPersonal)
  const error = fieldErrors(update.error)

  return (
    <FormDialog
      title="Edit personal details"
      wide
      error={update.error}
      pending={update.isPending}
      onClose={onClose}
      onSubmit={() => update.mutate({ id: employee.id, body: { givenName, familyName, personal: cleanPersonal(personal) } }, { onSuccess: onClose })}
    >
      <TextField label="Given name" required value={givenName} onChange={setGivenName} error={error('givenName')} />
      <TextField label="Family name" required value={familyName} onChange={setFamilyName} error={error('familyName')} />
      <PersonalSection value={personal} onChange={setPersonal} error={error} />
    </FormDialog>
  )
}
