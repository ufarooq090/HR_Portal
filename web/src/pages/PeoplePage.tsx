import {
  Badge,
  Body1,
  Button,
  SearchBox,
  Spinner,
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableHeaderCell,
  TableRow,
  Title2,
  makeStyles,
  tokens,
} from '@fluentui/react-components'
import { ArrowUploadRegular, PersonAddRegular } from '@fluentui/react-icons'
import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { useEmployees } from '../api/employees'
import { useCountries, useMe, useOrganisationItems } from '../api/queries'
import { type Department, type EmployeeFilters, type EmploymentStatus, type OfficeLocation, employmentStatuses } from '../api/types'
import { QueryError } from '../app/QueryError'
import { canCreateEmployees } from '../auth/roles'
import { SelectField } from './employees/fields'
import { label, statusColor } from './employees/format'
import { fieldGrid, tableWrap } from '../app/layout'

const useStyles = makeStyles({
  page: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL, maxWidth: '1280px' },
  heading: { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: tokens.spacingHorizontalM, flexWrap: 'wrap' },
  actions: { display: 'flex', gap: tokens.spacingHorizontalS, flexWrap: 'wrap' },
  filters: fieldGrid,
  search: { gridColumn: '1 / -1', maxWidth: '640px' },
  tableWrap,
})

const currentStatuses: EmploymentStatus[] = ['Probation', 'Confirmed', 'OnNotice']

/** Status filter values: "current" (default), "" (all) or a single status. */
function statusFilter(value: string): EmploymentStatus[] | undefined {
  if (value === 'current') return currentStatuses
  return value ? [value as EmploymentStatus] : undefined
}

/** Employee directory (EMP-08). The API returns only the people the signed-in user may see. */
export function PeoplePage() {
  const styles = useStyles()
  const navigate = useNavigate()
  const { data: me } = useMe()
  const [input, setInput] = useState('')
  const [search, setSearch] = useState('')
  const [departmentId, setDepartmentId] = useState('')
  const [officeLocationId, setOfficeLocationId] = useState('')
  const [countryCode, setCountryCode] = useState('')
  const [managerId, setManagerId] = useState('')
  const [status, setStatus] = useState('current')

  const filters: EmployeeFilters = { search, departmentId, officeLocationId, countryCode, managerId, status: statusFilter(status) }
  const { data: employees, isLoading, error } = useEmployees(filters)
  // Same request as the default view (current employees), so it is served from the cache.
  const { data: everyone } = useEmployees({ status: currentStatuses })
  const { data: departments } = useOrganisationItems<Department>('departments')
  const { data: offices } = useOrganisationItems<OfficeLocation>('locations')
  const { data: countries } = useCountries()

  useEffect(() => {
    const timer = setTimeout(() => setSearch(input.trim()), 250)
    return () => clearTimeout(timer)
  }, [input])

  const managers = useMemo(() => {
    const byId = new Map<string, string>()
    for (const e of everyone ?? []) if (e.managerId && e.managerName) byId.set(e.managerId, e.managerName)
    return [...byId].map(([value, name]) => ({ value, label: name })).sort((a, b) => a.label.localeCompare(b.label))
  }, [everyone])

  return (
    <section className={styles.page}>
      <div className={styles.heading}>
        <Title2 as="h1">People</Title2>
        {canCreateEmployees(me) && (
          <div className={styles.actions}>
            <Button icon={<ArrowUploadRegular />} onClick={() => navigate('/people/import')}>
              Import
            </Button>
            <Button appearance="primary" icon={<PersonAddRegular />} onClick={() => navigate('/people/new')}>
              Add employee
            </Button>
          </div>
        )}
      </div>
      <Body1>
        Everyone you have access to: HR sees their countries, managers their team, and everyone sees their own
        record.
      </Body1>

      <div className={styles.filters}>
        <SearchBox
          className={styles.search}
          placeholder="Name, employee ID, email or title"
          aria-label="Search people"
          value={input}
          onChange={(_, d) => setInput(d.value)}
        />
        <SelectField
          label="Status"
          value={status}
          onChange={setStatus}
          clearLabel="All statuses"
          options={[{ value: 'current', label: 'Current employees' }, ...employmentStatuses.map((s) => ({ value: s, label: label(s) }))]}
        />
        <SelectField
          label="Department"
          value={departmentId}
          onChange={setDepartmentId}
          clearLabel="All departments"
          options={(departments ?? []).map((d) => ({ value: d.id, label: d.name }))}
        />
        <SelectField
          label="Office"
          value={officeLocationId}
          onChange={setOfficeLocationId}
          clearLabel="All offices"
          options={(offices ?? []).map((o) => ({ value: o.id, label: `${o.name} (${o.countryCode})` }))}
        />
        <SelectField
          label="Country"
          value={countryCode}
          onChange={setCountryCode}
          clearLabel="All countries"
          options={(countries ?? []).map((c) => ({ value: c.countryCode, label: c.name }))}
        />
        <SelectField label="Manager" value={managerId} onChange={setManagerId} clearLabel="Any manager" options={managers} />
      </div>

      {isLoading && <Spinner label="Loading people…" />}
      {error && <QueryError error={error} />}
      {employees && (
        <>
          <Body1 aria-live="polite">{employees.length === 1 ? '1 person' : `${employees.length} people`}</Body1>
          <div className={styles.tableWrap}>
            <Table aria-label="People">
              <TableHeader>
                <TableRow>
                  <TableHeaderCell>Name</TableHeaderCell>
                  <TableHeaderCell>Employee ID</TableHeaderCell>
                  <TableHeaderCell>Designation</TableHeaderCell>
                  <TableHeaderCell>Department</TableHeaderCell>
                  <TableHeaderCell>Office</TableHeaderCell>
                  <TableHeaderCell>Manager</TableHeaderCell>
                  <TableHeaderCell>Status</TableHeaderCell>
                </TableRow>
              </TableHeader>
              <TableBody>
                {employees.map((e) => (
                  <TableRow key={e.id}>
                    <TableCell>
                      <Link to={`/people/${e.id}`}>{e.fullName}</Link>
                    </TableCell>
                    <TableCell>{e.employeeNumber}</TableCell>
                    <TableCell>{e.designation}</TableCell>
                    <TableCell>{e.departmentName}</TableCell>
                    <TableCell>
                      {e.officeLocationName} ({e.countryCode})
                    </TableCell>
                    <TableCell>{e.managerName ?? '—'}</TableCell>
                    <TableCell>
                      <Badge appearance="tint" color={statusColor(e.status)}>
                        {label(e.status)}
                      </Badge>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        </>
      )}
    </section>
  )
}
