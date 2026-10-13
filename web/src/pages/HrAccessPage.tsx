import {
  Body1,
  Button,
  Checkbox,
  Combobox,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Field,
  Label,
  MessageBar,
  MessageBarBody,
  Option,
  Spinner,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableHeaderCell,
  TableRow,
  Text,
  Title2,
  makeStyles,
  tokens,
} from '@fluentui/react-components'
import { ArrowResetRegular, EditRegular, PersonAddRegular } from '@fluentui/react-icons'
import { useId, useState } from 'react'
import { ApiError } from '../api/client'
import { useEmployees } from '../api/employees'
import { useCountries, useHrAccess, useMe, useResetHrAccess, useSaveHrAccess } from '../api/queries'
import type { HrAccess } from '../api/types'
import { QueryError } from '../app/QueryError'
import { describeCountries } from './hrAccess'

const useStyles = makeStyles({
  page: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL, maxWidth: '1000px' },
  heading: { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: tokens.spacingHorizontalM, flexWrap: 'wrap' },
  tableWrap: { overflowX: 'auto' },
  actions: { display: 'flex', gap: tokens.spacingHorizontalXS },
  form: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  countries: { display: 'flex', flexWrap: 'wrap', gap: tokens.spacingHorizontalM },
  error: { color: tokens.colorPaletteRedForeground1 },
})

/** HR Admins limit or extend HR users' country access and grant compensation access (MC-03). */
export function HrAccessPage() {
  const styles = useStyles()
  const { data: rows, isLoading, error } = useHrAccess()
  const reset = useResetHrAccess()
  const [editing, setEditing] = useState<{ row: HrAccess | null } | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  return (
    <section className={styles.page}>
      <div className={styles.heading}>
        <Title2 as="h1">HR data access</Title2>
        <Button appearance="primary" icon={<PersonAddRegular />} onClick={() => setEditing({ row: null })}>
          Set access
        </Button>
      </div>
      <Body1>
        By default an HR Admin sees every country and an HR Executive only their own country, without compensation.
        Set access here to limit or extend that for one person. You cannot change your own access or grant countries
        you cannot see yourself.
      </Body1>

      {notice && (
        <MessageBar intent="success">
          <MessageBarBody>{notice}</MessageBarBody>
        </MessageBar>
      )}
      {reset.error && <QueryError error={reset.error} />}
      {isLoading && <Spinner label="Loading…" />}
      {error && <QueryError error={error} />}
      {rows && rows.length === 0 && <Body1>Everyone has their default access.</Body1>}
      {rows && rows.length > 0 && (
        <div className={styles.tableWrap}>
          <Table aria-label="HR data access">
            <TableHeader>
              <TableRow>
                <TableHeaderCell>Person</TableHeaderCell>
                <TableHeaderCell>Home country</TableHeaderCell>
                <TableHeaderCell>Countries</TableHeaderCell>
                <TableHeaderCell>Compensation</TableHeaderCell>
                <TableHeaderCell aria-label="Actions" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {rows.map((r) => (
                <TableRow key={r.employeeId}>
                  <TableCell>
                    {r.employeeName} ({r.employeeNumber})
                  </TableCell>
                  <TableCell>{r.employeeCountryCode}</TableCell>
                  <TableCell>{describeCountries(r)}</TableCell>
                  <TableCell>{r.compensationAccess ? 'Granted' : 'No'}</TableCell>
                  <TableCell>
                    <div className={styles.actions}>
                      <Button
                        appearance="subtle"
                        icon={<EditRegular />}
                        aria-label={`Edit access for ${r.employeeName}`}
                        onClick={() => setEditing({ row: r })}
                      />
                      <Button
                        appearance="subtle"
                        icon={<ArrowResetRegular />}
                        aria-label={`Reset access for ${r.employeeName}`}
                        onClick={() =>
                          reset.mutate(r.employeeId, { onSuccess: () => setNotice(`${r.employeeName} is back to default access.`) })
                        }
                      />
                    </div>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}

      {editing && (
        <AccessDialog
          row={editing.row}
          onClose={(saved) => {
            setEditing(null)
            if (saved) setNotice(`Access saved for ${saved}.`)
          }}
        />
      )}
    </section>
  )
}

function AccessDialog({ row, onClose }: { row: HrAccess | null; onClose: (savedName?: string) => void }) {
  const styles = useStyles()
  const { data: me } = useMe()
  const { data: countries } = useCountries()
  const [query, setQuery] = useState('')
  const { data: people } = useEmployees()
  const save = useSaveHrAccess()
  const [employee, setEmployee] = useState<{ id: string; name: string } | null>(
    row ? { id: row.employeeId, name: row.employeeName } : null,
  )
  const [allCountries, setAllCountries] = useState(row?.allCountries ?? false)
  const [codes, setCodes] = useState<string[]>(row?.countryCodes ?? [])
  const [compensation, setCompensation] = useState(row?.compensationAccess ?? false)
  const apiError = save.error instanceof ApiError ? save.error : undefined
  const countriesLabelId = useId()

  const matches = (people ?? [])
    .filter((p) => p.id !== me?.employeeId)
    .filter((p) => `${p.fullName} ${p.employeeNumber}`.toLowerCase().includes(query.toLowerCase()))
    .slice(0, 50)

  const submit = () => {
    if (!employee) return
    save.mutate(
      { employeeId: employee.id, allCountries, countryCodes: allCountries ? [] : codes, compensationAccess: compensation },
      { onSuccess: () => onClose(employee.name) },
    )
  }

  return (
    <Dialog open onOpenChange={(_, data) => !data.open && onClose()}>
      <DialogSurface>
        <form
          onSubmit={(e) => {
            e.preventDefault()
            submit()
          }}
        >
          <DialogBody>
            <DialogTitle>{row ? `Access for ${row.employeeName}` : 'Set HR data access'}</DialogTitle>
            <DialogContent className={styles.form}>
              {!row && (
                <Field label="Person" required>
                  <Combobox
                    placeholder="Search by name or employee ID"
                    value={employee && !query ? employee.name : query}
                    onInput={(e) => setQuery((e.target as HTMLInputElement).value)}
                    onOptionSelect={(_, d) => {
                      const person = people?.find((p) => p.id === d.optionValue)
                      if (person) setEmployee({ id: person.id, name: person.fullName })
                      setQuery('')
                    }}
                  >
                    {matches.map((p) => (
                      <Option key={p.id} value={p.id} text={p.fullName}>
                        {`${p.fullName} (${p.employeeNumber})`}
                      </Option>
                    ))}
                  </Combobox>
                </Field>
              )}
              <Switch
                label="All countries"
                checked={allCountries}
                onChange={(_, d) => setAllCountries(d.checked)}
              />
              {/* A group rather than a Field: a Field gives every control inside it the same id. */}
              <div role="group" aria-labelledby={countriesLabelId}>
                <Label id={countriesLabelId}>Countries</Label>
                <div className={styles.countries}>
                  {(countries ?? []).map((c) => (
                    <Checkbox
                      key={c.countryCode}
                      label={`${c.name} (${c.countryCode})`}
                      disabled={allCountries}
                      checked={allCountries || codes.includes(c.countryCode)}
                      onChange={(_, d) =>
                        setCodes((current) =>
                          d.checked ? [...current, c.countryCode] : current.filter((x) => x !== c.countryCode),
                        )
                      }
                    />
                  ))}
                </div>
                {apiError?.fieldError('countryCodes') && (
                  <Text role="alert" className={styles.error}>
                    {apiError.fieldError('countryCodes')}
                  </Text>
                )}
              </div>
              <Checkbox
                label="Can view and edit compensation (HR Executives)"
                checked={compensation}
                onChange={(_, d) => setCompensation(d.checked === true)}
              />
              {save.error && !apiError?.problem?.errors && <QueryError error={save.error} />}
            </DialogContent>
            <DialogActions>
              <Button appearance="secondary" onClick={() => onClose()}>
                Cancel
              </Button>
              <Button appearance="primary" type="submit" disabled={!employee || save.isPending}>
                {save.isPending ? 'Saving…' : 'Save'}
              </Button>
            </DialogActions>
          </DialogBody>
        </form>
      </DialogSurface>
    </Dialog>
  )
}
