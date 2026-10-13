import {
  Badge,
  Body1,
  Button,
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
import { AddRegular } from '@fluentui/react-icons'
import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { useRequisitions } from '../../api/requisitions'
import { type RequisitionStatus, requisitionStatuses } from '../../api/types'
import { QueryError } from '../../app/QueryError'
import { SelectField } from '../employees/fields'
import { formatDate, label } from '../employees/format'
import { budgetRange, requisitionStatusColor } from './requisitionFormat'

const useStyles = makeStyles({
  page: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL, maxWidth: '1280px' },
  heading: { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: tokens.spacingHorizontalM, flexWrap: 'wrap' },
  filter: { maxWidth: '260px' },
  tableWrap: { overflowX: 'auto' },
})

/** REQ-01/04. Managers see their department's requisitions; HR sees all within their countries. */
export function RequisitionsPage() {
  const styles = useStyles()
  const navigate = useNavigate()
  const [status, setStatus] = useState('')
  const { data, isLoading, error } = useRequisitions(status ? [status as RequisitionStatus] : [])

  return (
    <section className={styles.page}>
      <div className={styles.heading}>
        <Title2 as="h1">Requisitions</Title2>
        <Button appearance="primary" icon={<AddRegular />} onClick={() => navigate('/requisitions/new')}>
          Raise requisition
        </Button>
      </div>
      <Body1>Every hire starts as an approved requisition. Managers see their own department's; HR sees all in their countries.</Body1>
      <div className={styles.filter}>
        <SelectField
          label="Status"
          value={status}
          onChange={setStatus}
          clearLabel="All statuses"
          options={requisitionStatuses.map((s) => ({ value: s, label: label(s) }))}
        />
      </div>

      {isLoading && <Spinner label="Loading requisitions…" />}
      {error && <QueryError error={error} />}
      {data && data.length === 0 && <Body1>No requisitions yet.</Body1>}
      {data && data.length > 0 && (
        <div className={styles.tableWrap}>
          <Table aria-label="Requisitions">
            <TableHeader>
              <TableRow>
                <TableHeaderCell>Requisition</TableHeaderCell>
                <TableHeaderCell>Department</TableHeaderCell>
                <TableHeaderCell>Office</TableHeaderCell>
                <TableHeaderCell>Openings</TableHeaderCell>
                <TableHeaderCell>Budget (annual)</TableHeaderCell>
                <TableHeaderCell>Target date</TableHeaderCell>
                <TableHeaderCell>Requested by</TableHeaderCell>
                <TableHeaderCell>Status</TableHeaderCell>
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
                  <TableCell>{r.departmentName}</TableCell>
                  <TableCell>
                    {r.officeLocationName} ({r.countryCode})
                  </TableCell>
                  <TableCell>{r.openings}</TableCell>
                  <TableCell>{budgetRange(r.budgetMin, r.budgetMax, r.currencyCode)}</TableCell>
                  <TableCell>{formatDate(r.targetDate)}</TableCell>
                  <TableCell>{r.requesterName}</TableCell>
                  <TableCell>
                    <Badge appearance="tint" color={requisitionStatusColor(r.status)}>
                      {label(r.status)}
                    </Badge>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </section>
  )
}
