import {
  Badge,
  Body1,
  Button,
  Caption1,
  Card,
  makeStyles,
  Spinner,
  Tab,
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableHeaderCell,
  TableRow,
  TabList,
  Subtitle2,
  Text,
  Title2,
  tokens,
} from '@fluentui/react-components'
import { DismissRegular, EditRegular, EyeRegular } from '@fluentui/react-icons'
import { type ReactNode, useState } from 'react'
import { Link, useParams } from 'react-router'
import { useCancelScheduledChange, useEmployee, useEmployeeHistory, usePersonal } from '../../api/employees'
import type { EmployeeDetail, ScheduledChange } from '../../api/types'
import { useMe } from '../../api/queries'
import { QueryError } from '../../app/QueryError'
import { isHr } from '../../auth/roles'
import { EmailHistoryCard, SendEmailDialog } from '../templates/EmailComponents'
import { CompensationTab } from './CompensationTab'
import { DocumentsTab } from './DocumentsTab'
import { JobChangeDialog, ManagerDialog, PersonalDialog, StatusDialog } from './EmployeeDialogs'
import { formatDate, label, statusColor } from './format'

const useStyles = makeStyles({
  page: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL, maxWidth: '1100px' },
  header: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalM, flexWrap: 'wrap' },
  facts: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))',
    gap: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalL}`,
    margin: 0,
  },
  fact: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXXS, margin: 0 },
  term: { color: tokens.colorNeutralForeground3 },
  actions: { display: 'flex', gap: tokens.spacingHorizontalS, flexWrap: 'wrap' },
  tableWrap: { overflowX: 'auto' },
  note: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS, color: tokens.colorNeutralForeground3 },
  upcoming: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM, padding: tokens.spacingHorizontalL },
  upcomingRow: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalM, flexWrap: 'wrap' },
})

type TabId = 'overview' | 'personal' | 'history' | 'compensation' | 'documents' | 'emails'
type DialogId = 'job' | 'manager' | 'status' | 'email' | null

function Facts({ items }: { items: [string, ReactNode][] }) {
  const styles = useStyles()
  return (
    <dl className={styles.facts}>
      {items.map(([term, value]) => (
        <div key={term} className={styles.fact}>
          <dt className={styles.term}>
            <Caption1>{term}</Caption1>
          </dt>
          <dd className={styles.fact}>
            <Text>{value ?? '—'}</Text>
          </dd>
        </div>
      ))}
    </dl>
  )
}

/** One employee's record. Tabs and actions follow the permissions the API computed for the signed-in user. */
export function EmployeeProfilePage() {
  const styles = useStyles()
  const { id = '' } = useParams()
  const { data: employee, isLoading, error } = useEmployee(id)
  const [tab, setTab] = useState<TabId>('overview')
  const [dialog, setDialog] = useState<DialogId>(null)
  const [editing, setEditing] = useState<ScheduledChange | null>(null)
  const { data: me } = useMe()

  if (isLoading) return <Spinner label="Loading…" />
  if (error || !employee) return <QueryError error={error} />
  const p = employee.permissions

  return (
    <section className={styles.page}>
      <Body1>
        <Link to="/people">Back to People</Link>
      </Body1>
      <div className={styles.header}>
        <Title2 as="h1">
          {employee.givenName} {employee.familyName}
        </Title2>
        <Badge appearance="tint" color={statusColor(employee.status)}>
          {label(employee.status)}
        </Badge>
      </div>
      <Body1>
        {employee.designation} · {employee.departmentName} · {employee.employeeNumber}
      </Body1>

      <TabList selectedValue={tab} onTabSelect={(_, d) => setTab(d.value as TabId)}>
        <Tab value="overview">Job</Tab>
        {p.canViewPersonal && <Tab value="personal">Personal</Tab>}
        <Tab value="history">History</Tab>
        {p.canViewCompensation && <Tab value="compensation">Compensation</Tab>}
        {p.canViewDocuments && <Tab value="documents">Documents</Tab>}
        {p.canEdit && isHr(me) && <Tab value="emails">Emails</Tab>}
      </TabList>

      {tab === 'overview' && <JobTab employee={employee} onAction={setDialog} onEditScheduled={setEditing} />}
      {tab === 'personal' && p.canViewPersonal && <PersonalTab employee={employee} />}
      {tab === 'history' && <HistoryTab employeeId={employee.id} />}
      {tab === 'compensation' && p.canViewCompensation && <CompensationTab employee={employee} />}
      {tab === 'documents' && p.canViewDocuments && <DocumentsTab employee={employee} />}
      {tab === 'emails' && p.canEdit && isHr(me) && <EmailHistoryCard person={{ employeeId: employee.id }} />}
      {dialog === 'email' && (
        <SendEmailDialog
          record={{
            recordType: 'Employee',
            recordId: employee.id,
            countryCode: employee.countryCode,
            name: `${employee.givenName} ${employee.familyName}`,
            historyKey: employee.id,
          }}
          onClose={() => setDialog(null)}
        />
      )}

      {dialog === 'job' && <JobChangeDialog employee={employee} onClose={() => setDialog(null)} />}
      {dialog === 'manager' && <ManagerDialog employee={employee} onClose={() => setDialog(null)} />}
      {dialog === 'status' && <StatusDialog employee={employee} onClose={() => setDialog(null)} />}
      {editing?.kind === 'ManagerChange' && <ManagerDialog employee={employee} scheduled={editing} onClose={() => setEditing(null)} />}
      {editing && editing.kind !== 'ManagerChange' && <JobChangeDialog employee={employee} scheduled={editing} onClose={() => setEditing(null)} />}
    </section>
  )
}

function JobTab({
  employee,
  onAction,
  onEditScheduled,
}: {
  employee: EmployeeDetail
  onAction: (dialog: DialogId) => void
  onEditScheduled: (change: ScheduledChange) => void
}) {
  const styles = useStyles()
  const active = employee.nextStatuses.length > 0
  return (
    <>
      {employee.upcoming.length > 0 && <UpcomingChanges employee={employee} onEdit={onEditScheduled} />}
      {employee.permissions.canEdit && (
        <div className={styles.actions}>
          <Button onClick={() => onAction('email')}>Send email</Button>
        </div>
      )}
      {employee.permissions.canEdit && active && (
        <div className={styles.actions}>
          <Button onClick={() => onAction('job')}>Change job</Button>
          <Button onClick={() => onAction('manager')}>Change manager</Button>
          <Button onClick={() => onAction('status')}>Change status</Button>
        </div>
      )}
      <Facts
        items={[
          ['Employee ID', employee.employeeNumber],
          ['Work email', employee.workEmail],
          ['Designation', employee.designation],
          ['Department', employee.departmentName],
          ['Office', `${employee.officeLocationName} (${employee.countryCode})`],
          ['Legal entity', employee.legalEntityName],
          ['Grade', employee.gradeName],
          ['Reporting manager', employee.managerId ? <Link key="manager" to={`/people/${employee.managerId}`}>{employee.managerName}</Link> : null],
          ['Employment type', label(employee.employmentType)],
          ['Joining date', formatDate(employee.joiningDate)],
          ['Probation ends', employee.probationEndDate ? formatDate(employee.probationEndDate) : null],
          ['Last working day', employee.lastWorkingDate ? formatDate(employee.lastWorkingDate) : null],
        ]}
      />
    </>
  )
}

/** Future-dated changes: HR can edit or cancel them until they take effect; failed ones explain why. */
function UpcomingChanges({ employee, onEdit }: { employee: EmployeeDetail; onEdit: (change: ScheduledChange) => void }) {
  const styles = useStyles()
  const cancel = useCancelScheduledChange()
  const summary = (c: ScheduledChange) =>
    c.kind === 'ManagerChange'
      ? `Manager: ${c.newManagerName ?? 'no manager'}`
      : [c.designation, c.departmentName, c.officeLocationName, c.gradeName].filter(Boolean).join(' · ')

  return (
    <Card className={styles.upcoming} aria-label="Upcoming changes">
      <Subtitle2 as="h2">Upcoming changes</Subtitle2>
      {cancel.error && <QueryError error={cancel.error} />}
      {employee.upcoming.map((c) => (
        <div key={c.id} className={styles.upcomingRow} role="group" aria-label={`${label(c.kind)} on ${formatDate(c.effectiveDate)}`}>
          <Badge appearance="tint" color={c.status === 'Failed' ? 'danger' : 'informative'}>
            {c.status === 'Failed' ? 'Not applied' : 'Upcoming'}
          </Badge>
          <Text weight="semibold">
            {label(c.kind)} on {formatDate(c.effectiveDate)}
          </Text>
          <Text>{summary(c)}</Text>
          {c.status === 'Failed' && <Caption1>{c.outcome}</Caption1>}
          {c.status === 'Pending' && employee.permissions.canEdit && (
            <div className={styles.actions}>
              <Button size="small" icon={<EditRegular />} onClick={() => onEdit(c)}>
                Edit
              </Button>
              <Button
                size="small"
                icon={<DismissRegular />}
                disabled={cancel.isPending}
                onClick={() => cancel.mutate({ id: employee.id, changeId: c.id })}
              >
                Cancel change
              </Button>
            </div>
          )}
        </div>
      ))}
    </Card>
  )
}

/**
 * Fetched only when the tab is opened: when a manager views a team member's personal details the API records it
 * in the audit log. National ID is shown to HR and the employee only.
 */
function PersonalTab({ employee }: { employee: EmployeeDetail }) {
  const styles = useStyles()
  const { data: d, isLoading, error } = usePersonal(employee.id, true)
  const [editing, setEditing] = useState(false)
  if (isLoading) return <Spinner label="Loading…" />
  if (error || !d) return <QueryError error={error} />
  const address = [d.addressLine1, d.addressLine2, d.city, d.region, d.postalCode, d.addressCountryCode].filter(Boolean).join(', ')
  const p = employee.permissions

  return (
    <>
      {!p.canViewNationalId && (
        <Body1 className={styles.note}>
          <EyeRegular /> You can see this as {employee.givenName}'s manager. Each view is recorded in the audit log.
        </Body1>
      )}
      {p.canEditPersonal && (
        <div className={styles.actions}>
          <Button icon={<EditRegular />} onClick={() => setEditing(true)}>
            Edit personal details
          </Button>
        </div>
      )}
      <Facts
        items={[
          ['Personal email', d.personalEmail],
          ['Phone', d.phone],
          ['Date of birth', d.dateOfBirth ? formatDate(d.dateOfBirth) : null],
          ...(p.canViewNationalId ? [['National ID', d.nationalId] as [string, ReactNode]] : []),
          ['Address', address || null],
          ...d.emergencyContacts.map((c, i): [string, ReactNode] => [`Emergency contact ${i + 1}`, `${c.name} (${c.relationship}) · ${c.phone}`]),
        ]}
      />
      {editing && <PersonalDialog employee={employee} personal={d} onClose={() => setEditing(false)} />}
    </>
  )
}

/** EMP-05: dated job and status history. */
function HistoryTab({ employeeId }: { employeeId: string }) {
  const styles = useStyles()
  const { data: history, isLoading, error } = useEmployeeHistory(employeeId)
  if (isLoading) return <Spinner label="Loading history…" />
  if (error) return <QueryError error={error} />
  return (
    <div className={styles.tableWrap}>
      <Table aria-label="Job history">
        <TableHeader>
          <TableRow>
            <TableHeaderCell>Effective</TableHeaderCell>
            <TableHeaderCell>Change</TableHeaderCell>
            <TableHeaderCell>Designation</TableHeaderCell>
            <TableHeaderCell>Department</TableHeaderCell>
            <TableHeaderCell>Office</TableHeaderCell>
            <TableHeaderCell>Manager</TableHeaderCell>
            <TableHeaderCell>Status</TableHeaderCell>
            <TableHeaderCell>Reason</TableHeaderCell>
          </TableRow>
        </TableHeader>
        <TableBody>
          {(history ?? []).map((h) => (
            <TableRow key={h.id}>
              <TableCell>{formatDate(h.effectiveDate)}</TableCell>
              <TableCell>{label(h.kind)}</TableCell>
              <TableCell>{h.designation}</TableCell>
              <TableCell>{h.departmentName}</TableCell>
              <TableCell>{h.officeLocationName}</TableCell>
              <TableCell>{h.managerName ?? '—'}</TableCell>
              <TableCell>{label(h.status)}</TableCell>
              <TableCell>{h.reason ?? ''}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  )
}
