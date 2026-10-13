import {
  Body1,
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
import { useEffect, useState } from 'react'
import { useEmployees } from '../api/queries'
import { QueryError } from '../app/QueryError'

const useStyles = makeStyles({
  page: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL, maxWidth: '1200px' },
  search: { maxWidth: '360px' },
  tableWrap: { overflowX: 'auto' },
})

/** Employee directory (EMP-08). The API returns only the people the signed-in user may see. */
export function PeoplePage() {
  const styles = useStyles()
  const [input, setInput] = useState('')
  const [search, setSearch] = useState('')
  const { data: employees, isLoading, error } = useEmployees(search)

  useEffect(() => {
    const timer = setTimeout(() => setSearch(input.trim()), 250)
    return () => clearTimeout(timer)
  }, [input])

  return (
    <section className={styles.page}>
      <Title2 as="h1">People</Title2>
      <Body1>
        Everyone you have access to: HR sees their countries, managers their team, and everyone sees their own
        record.
      </Body1>
      <SearchBox
        className={styles.search}
        placeholder="Search by name, employee ID or email"
        aria-label="Search people"
        value={input}
        onChange={(_, d) => setInput(d.value)}
      />

      {isLoading && <Spinner label="Loading people…" />}
      {error && <QueryError error={error} />}
      {employees && (
        <>
          <Body1 aria-live="polite">
            {employees.length === 1 ? '1 person' : `${employees.length} people`}
          </Body1>
          <div className={styles.tableWrap}>
            <Table aria-label="People">
              <TableHeader>
                <TableRow>
                  <TableHeaderCell>Name</TableHeaderCell>
                  <TableHeaderCell>Employee ID</TableHeaderCell>
                  <TableHeaderCell>Job title</TableHeaderCell>
                  <TableHeaderCell>Department</TableHeaderCell>
                  <TableHeaderCell>Office</TableHeaderCell>
                  <TableHeaderCell>Manager</TableHeaderCell>
                </TableRow>
              </TableHeader>
              <TableBody>
                {employees.map((e) => (
                  <TableRow key={e.id}>
                    <TableCell>{e.fullName}</TableCell>
                    <TableCell>{e.employeeNumber}</TableCell>
                    <TableCell>{e.jobTitle}</TableCell>
                    <TableCell>{e.departmentName}</TableCell>
                    <TableCell>
                      {e.officeLocationName} ({e.countryCode})
                    </TableCell>
                    <TableCell>{e.managerName ?? '—'}</TableCell>
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
