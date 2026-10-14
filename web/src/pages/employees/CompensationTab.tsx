import {
  Badge,
  Body1,
  Button,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  makeStyles,
  Spinner,
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableHeaderCell,
  TableRow,
  tokens,
} from '@fluentui/react-components'
import { AddRegular, LockClosedRegular } from '@fluentui/react-icons'
import { useState } from 'react'
import { ApiError } from '../../api/client'
import { useAddCompensation, useCompensation } from '../../api/employees'
import { useCountries } from '../../api/queries'
import { type CompensationReason, compensationReasons, type EmployeeDetail } from '../../api/types'
import { QueryError } from '../../app/QueryError'
import { DateField, SelectField, TextField } from './fields'
import { formatDate, formatMoney, label, todayIso } from './format'

const useStyles = makeStyles({
  section: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  note: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS, color: tokens.colorNeutralForeground3 },
  form: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  tableWrap: { overflowX: 'auto' },
})

/**
 * EMP-03: salary history in local currency (MC-02). Mounted only when the tab is opened, because each fetch is
 * recorded in the audit log as a view of salary data.
 */
export function CompensationTab({ employee }: { employee: EmployeeDetail }) {
  const styles = useStyles()
  const { data: records, isLoading, error } = useCompensation(employee.id, true)
  const [adding, setAdding] = useState(false)

  return (
    <div className={styles.section}>
      <Body1 className={styles.note}>
        <LockClosedRegular /> Confidential. Each time this is opened it is recorded in the audit log.
      </Body1>
      {employee.permissions.canEditCompensation && (
        <div>
          <Button icon={<AddRegular />} onClick={() => setAdding(true)}>
            Add compensation
          </Button>
        </div>
      )}
      {isLoading && <Spinner label="Loading…" />}
      {error && <QueryError error={error} />}
      {records && records.length === 0 && <Body1>No compensation recorded yet.</Body1>}
      {records && records.length > 0 && (
        <div className={styles.tableWrap}>
          <Table aria-label="Compensation history">
            <TableHeader>
              <TableRow>
                <TableHeaderCell>Effective</TableHeaderCell>
                <TableHeaderCell>Annual CTC</TableHeaderCell>
                <TableHeaderCell>Band</TableHeaderCell>
                <TableHeaderCell>Reason</TableHeaderCell>
                <TableHeaderCell>Note</TableHeaderCell>
              </TableRow>
            </TableHeader>
            <TableBody>
              {records.map((r) => (
                <TableRow key={r.id}>
                  <TableCell>
                    {formatDate(r.effectiveDate)}{' '}
                    {r.isCurrent && (
                      <Badge appearance="tint" color="success">
                        Current
                      </Badge>
                    )}
                  </TableCell>
                  <TableCell>{formatMoney(r.annualCtc, r.currencyCode)}</TableCell>
                  <TableCell>{r.salaryBand ?? '—'}</TableCell>
                  <TableCell>{label(r.reason)}</TableCell>
                  <TableCell>{r.note ?? ''}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
      {adding && <AddCompensationDialog employee={employee} hasRecords={(records?.length ?? 0) > 0} onClose={() => setAdding(false)} />}
    </div>
  )
}

function AddCompensationDialog({ employee, hasRecords, onClose }: { employee: EmployeeDetail; hasRecords: boolean; onClose: () => void }) {
  const styles = useStyles()
  const add = useAddCompensation()
  const { data: countries } = useCountries()
  const currency = countries?.find((c) => c.countryCode === employee.countryCode)?.currencyCode ?? ''
  const [effectiveDate, setEffectiveDate] = useState(hasRecords ? todayIso() : employee.joiningDate)
  const [amount, setAmount] = useState('')
  const [band, setBand] = useState('')
  const [reason, setReason] = useState<CompensationReason>(hasRecords ? 'Increment' : 'Joining')
  const [note, setNote] = useState('')
  const apiError = add.error instanceof ApiError ? add.error : undefined
  const parsed = Number(amount.replace(/,/g, ''))

  return (
    <Dialog open onOpenChange={(_, d) => !d.open && onClose()}>
      <DialogSurface>
        <form
          onSubmit={(e) => {
            e.preventDefault()
            add.mutate(
              {
                id: employee.id,
                body: {
                  effectiveDate,
                  annualCtc: Number.isFinite(parsed) ? parsed : 0,
                  currencyCode: currency,
                  salaryBand: band || null,
                  reason,
                  note: note || null,
                },
              },
              { onSuccess: onClose },
            )
          }}
        >
          <DialogBody>
            <DialogTitle>Add compensation</DialogTitle>
            <DialogContent className={styles.form}>
              <TextField
                label={`Annual CTC (${currency})`}
                required
                value={amount}
                onChange={setAmount}
                hint={`Recorded in ${currency}, the currency of ${employee.countryCode}.`}
                error={apiError?.fieldError('annualCtc') ?? apiError?.fieldError('currencyCode')}
              />
              <DateField label="Effective date" required value={effectiveDate} onChange={setEffectiveDate} error={apiError?.fieldError('effectiveDate')} />
              <SelectField
                label="Reason"
                required
                value={reason}
                onChange={(v) => setReason(v as CompensationReason)}
                options={compensationReasons.map((r) => ({ value: r, label: label(r) }))}
              />
              <TextField label="Salary band" value={band} onChange={setBand} error={apiError?.fieldError('salaryBand')} />
              <TextField label="Note" multiline value={note} onChange={setNote} error={apiError?.fieldError('note')} />
              {add.error && !apiError?.problem?.errors ? <QueryError error={add.error} /> : null}
            </DialogContent>
            <DialogActions>
              <Button onClick={onClose}>Cancel</Button>
              <Button appearance="primary" type="submit" disabled={add.isPending || !currency}>
                {add.isPending ? 'Saving…' : 'Save'}
              </Button>
            </DialogActions>
          </DialogBody>
        </form>
      </DialogSurface>
    </Dialog>
  )
}
