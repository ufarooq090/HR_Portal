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
import { useChangeJob, useChangeManager, useChangeStatus, useUpdatePersonal } from '../../api/employees'
import type { EmployeeDetail, EmploymentStatus, JobChangeKind, PersonalDetails } from '../../api/types'
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
export function JobChangeDialog({ employee, onClose }: { employee: EmployeeDetail; onClose: () => void }) {
  const change = useChangeJob()
  const [kind, setKind] = useState<JobChangeKind>('Promotion')
  const [effectiveDate, setEffectiveDate] = useState(todayIso())
  const [reason, setReason] = useState('')
  const [placement, setPlacement] = useState<Placement>({
    designation: employee.designation,
    legalEntityId: employee.legalEntityId,
    officeLocationId: employee.officeLocationId,
    departmentId: employee.departmentId,
    gradeId: employee.gradeId ?? '',
  })
  const error = fieldErrors(change.error)

  return (
    <FormDialog
      title="Change job"
      wide
      error={change.error}
      pending={change.isPending}
      onClose={onClose}
      onSubmit={() =>
        change.mutate(
          { id: employee.id, body: { kind, effectiveDate, ...placement, gradeId: placement.gradeId || null, reason: reason || null } },
          { onSuccess: onClose },
        )
      }
    >
      <SelectField
        label="Type of change"
        required
        value={kind}
        onChange={(v) => setKind(v as JobChangeKind)}
        options={(['Promotion', 'Transfer', 'Redesignation', 'Correction'] as const).map((k) => ({ value: k, label: label(k) }))}
        error={error('kind')}
      />
      <TextField label="Effective date" type="date" required value={effectiveDate} onChange={setEffectiveDate} error={error('effectiveDate')} />
      <PlacementSection value={placement} onChange={setPlacement} error={error} />
      <TextField label="Reason" multiline value={reason} onChange={setReason} error={error('reason')} />
    </FormDialog>
  )
}

export function ManagerDialog({ employee, onClose }: { employee: EmployeeDetail; onClose: () => void }) {
  const change = useChangeManager()
  const [managerId, setManagerId] = useState(employee.managerId ?? '')
  const [effectiveDate, setEffectiveDate] = useState(todayIso())
  const [reason, setReason] = useState('')
  const error = fieldErrors(change.error)

  return (
    <FormDialog
      title="Change manager"
      error={change.error}
      pending={change.isPending}
      onClose={onClose}
      onSubmit={() =>
        change.mutate({ id: employee.id, body: { managerId: managerId || null, effectiveDate, reason: reason || null } }, { onSuccess: onClose })
      }
    >
      <ManagerField value={managerId} onChange={setManagerId} excludeId={employee.id} error={error('managerId')} />
      <TextField label="Effective date" type="date" required value={effectiveDate} onChange={setEffectiveDate} error={error('effectiveDate')} />
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

export function PersonalDialog({ employee, onClose }: { employee: EmployeeDetail; onClose: () => void }) {
  const update = useUpdatePersonal()
  const [givenName, setGivenName] = useState(employee.givenName)
  const [familyName, setFamilyName] = useState(employee.familyName)
  const [personal, setPersonal] = useState<PersonalDetails>(employee.personal ?? emptyPersonal)
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
