import {
  Body1,
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
import { Link } from 'react-router'
import { useApprovals } from '../../api/requisitions'
import { QueryError } from '../../app/QueryError'
import { formatDate } from '../employees/format'
import { budgetRange } from './requisitionFormat'

const useStyles = makeStyles({
  page: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL, maxWidth: '1100px' },
  tableWrap: { overflowX: 'auto' },
})

/** REQ-02: requisitions waiting for the signed-in user's decision. */
export function ApprovalsPage() {
  const styles = useStyles()
  const { data, isLoading, error } = useApprovals()

  return (
    <section className={styles.page}>
      <Title2 as="h1">Approvals</Title2>
      {isLoading && <Spinner label="Loading…" />}
      {error && <QueryError error={error} />}
      {data && data.length === 0 && <Body1>Nothing is waiting for your approval.</Body1>}
      {data && data.length > 0 && (
        <div className={styles.tableWrap}>
          <Table aria-label="Waiting for your approval">
            <TableHeader>
              <TableRow>
                <TableHeaderCell>Requisition</TableHeaderCell>
                <TableHeaderCell>Department</TableHeaderCell>
                <TableHeaderCell>Openings</TableHeaderCell>
                <TableHeaderCell>Budget (annual)</TableHeaderCell>
                <TableHeaderCell>Target date</TableHeaderCell>
                <TableHeaderCell>Requested by</TableHeaderCell>
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.map((r) => (
                <TableRow key={r.id}>
                  <TableCell>
                    <Link to={`/requisitions/${r.id}`}>
                      {r.number} {r.title}
                    </Link>
                  </TableCell>
                  <TableCell>
                    {r.departmentName} ({r.countryCode})
                  </TableCell>
                  <TableCell>{r.openings}</TableCell>
                  <TableCell>{budgetRange(r.budgetMin, r.budgetMax, r.currencyCode)}</TableCell>
                  <TableCell>{formatDate(r.targetDate)}</TableCell>
                  <TableCell>{r.requesterName}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </section>
  )
}
