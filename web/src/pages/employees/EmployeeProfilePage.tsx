import {
  Badge,
  Body1,
  Button,
  Caption1,
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
  Text,
  Title2,
  tokens,
} from '@fluentui/react-components'
import { EditRegular } from '@fluentui/react-icons'
import { type ReactNode, useState } from 'react'
import { Link, useParams } from 'react-router'
import { useEmployee, useEmployeeHistory } from '../../api/employees'
import type { EmployeeDetail } from '../../api/types'
import { QueryError } from '../../app/QueryError'
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
})

type TabId = 'overview' | 'personal' | 'history' | 'compensation' | 'documents'
type DialogId = 'job' | 'manager' | 'status' | 'personal' | null

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
        {p.canViewPersonal && <Tab value="documents">Documents</Tab>}
      </TabList>

      {tab === 'overview' && <JobTab employee={employee} onAction={setDialog} />}
      {tab === 'personal' && p.canViewPersonal && <PersonalTab employee={employee} onEdit={() => setDialog('personal')} />}
      {tab === 'history' && <HistoryTab employeeId={employee.id} />}
      {tab === 'compensation' && p.canViewCompensation && <CompensationTab employee={employee} />}
      {tab === 'documents' && p.canViewPersonal && <DocumentsTab employee={employee} />}

      {dialog === 'job' && <JobChangeDialog employee={employee} onClose={() => setDialog(null)} />}
      {dialog === 'manager' && <ManagerDialog employee={employee} onClose={() => setDialog(null)} />}
      {dialog === 'status' && <StatusDialog employee={employee} onClose={() => setDialog(null)} />}
      {dialog === 'personal' && <PersonalDialog employee={employee} onClose={() => setDialog(null)} />}
    </section>
  )
}

function JobTab({ employee, onAction }: { employee: EmployeeDetail; onAction: (dialog: DialogId) => void }) {
  const styles = useStyles()
  const active = employee.nextStatuses.length > 0
  return (
    <>
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

function PersonalTab({ employee, onEdit }: { employee: EmployeeDetail; onEdit: () => void }) {
  const styles = useStyles()
  const d = employee.personal!
  const address = [d.addressLine1, d.addressLine2, d.city, d.region, d.postalCode, d.addressCountryCode].filter(Boolean).join(', ')
  return (
    <>
      {employee.permissions.canEditPersonal && (
        <div className={styles.actions}>
          <Button icon={<EditRegular />} onClick={onEdit}>
            Edit personal details
          </Button>
        </div>
      )}
      <Facts
        items={[
          ['Personal email', d.personalEmail],
          ['Phone', d.phone],
          ['Date of birth', d.dateOfBirth ? formatDate(d.dateOfBirth) : null],
          ['National ID', d.nationalId],
          ['Address', address || null],
          ...d.emergencyContacts.map((c, i): [string, ReactNode] => [`Emergency contact ${i + 1}`, `${c.name} (${c.relationship}) · ${c.phone}`]),
        ]}
      />
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
