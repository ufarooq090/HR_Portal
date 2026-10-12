import {
  Body1,
  Button,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Field,
  MessageBar,
  MessageBarBody,
  SpinButton,
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
import { EditRegular } from '@fluentui/react-icons'
import { useState } from 'react'
import { ApiError } from '../api/client'
import { useCountries, useMe, useUpdateCountry } from '../api/queries'
import type { CountrySettings } from '../api/types'
import { QueryError } from '../app/QueryError'
import { canManageCountrySettings } from '../auth/roles'

const useStyles = makeStyles({
  page: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL, maxWidth: '880px' },
  tableWrap: { overflowX: 'auto' },
  form: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
})

export function CountrySettingsPage() {
  const styles = useStyles()
  const { data: me } = useMe()
  const { data: countries, isLoading, error } = useCountries()
  const [editing, setEditing] = useState<CountrySettings | null>(null)
  const [saved, setSaved] = useState<string | null>(null)
  const canEdit = canManageCountrySettings(me)

  return (
    <section className={styles.page}>
      <Title2 as="h1">Countries</Title2>
      <Body1>Employment defaults applied per country. Leave, holidays and templates will be configured here too.</Body1>

      {saved && (
        <MessageBar intent="success">
          <MessageBarBody>{saved} settings saved.</MessageBarBody>
        </MessageBar>
      )}
      {isLoading && <Spinner label="Loading countries…" />}
      {error && <QueryError error={error} />}

      {countries && (
        <div className={styles.tableWrap}>
          <Table aria-label="Country settings">
            <TableHeader>
              <TableRow>
                <TableHeaderCell>Country</TableHeaderCell>
                <TableHeaderCell>Currency</TableHeaderCell>
                <TableHeaderCell>Probation</TableHeaderCell>
                <TableHeaderCell>Notice period</TableHeaderCell>
                {canEdit && <TableHeaderCell aria-label="Actions" />}
              </TableRow>
            </TableHeader>
            <TableBody>
              {countries.map((c) => (
                <TableRow key={c.countryCode}>
                  <TableCell>
                    {c.name} ({c.countryCode})
                  </TableCell>
                  <TableCell>{c.currencyCode}</TableCell>
                  <TableCell>{c.probationMonths} months</TableCell>
                  <TableCell>{c.noticePeriodDays} days</TableCell>
                  {canEdit && (
                    <TableCell>
                      <Button
                        icon={<EditRegular />}
                        appearance="subtle"
                        aria-label={`Edit ${c.name}`}
                        onClick={() => {
                          setSaved(null)
                          setEditing(c)
                        }}
                      >
                        Edit
                      </Button>
                    </TableCell>
                  )}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}

      {editing && (
        <EditCountryDialog
          country={editing}
          onClose={(savedName) => {
            setEditing(null)
            if (savedName) setSaved(savedName)
          }}
        />
      )}
    </section>
  )
}

/** SpinButton reports either a stepped value or typed text; ignore text that is not a whole number. */
function spinValue(data: { value?: number | null; displayValue?: string }, current: number): number {
  if (data.value != null) return data.value
  const typed = Number.parseInt(data.displayValue ?? '', 10)
  return Number.isNaN(typed) ? current : typed
}

function EditCountryDialog({ country, onClose }: { country: CountrySettings; onClose: (savedName?: string) => void }) {
  const styles = useStyles()
  const update = useUpdateCountry()
  const [probationMonths, setProbationMonths] = useState(country.probationMonths)
  const [noticePeriodDays, setNoticePeriodDays] = useState(country.noticePeriodDays)
  const apiError = update.error instanceof ApiError ? update.error : undefined

  const submit = () =>
    update.mutate(
      { countryCode: country.countryCode, probationMonths, noticePeriodDays },
      { onSuccess: () => onClose(country.name) },
    )

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
            <DialogTitle>{country.name}</DialogTitle>
            <DialogContent className={styles.form}>
              <Field
                label="Probation (months)"
                validationMessage={apiError?.fieldError('probationMonths')}
              >
                <SpinButton
                  value={probationMonths}
                  min={0}
                  max={24}
                  onChange={(_, d) => setProbationMonths((current) => spinValue(d, current))}
                />
              </Field>
              <Field
                label="Notice period (days)"
                validationMessage={apiError?.fieldError('noticePeriodDays')}
              >
                <SpinButton
                  value={noticePeriodDays}
                  min={0}
                  max={365}
                  onChange={(_, d) => setNoticePeriodDays((current) => spinValue(d, current))}
                />
              </Field>
              {update.error && !apiError?.problem?.errors && <QueryError error={update.error} />}
            </DialogContent>
            <DialogActions>
              <Button appearance="secondary" onClick={() => onClose()}>
                Cancel
              </Button>
              <Button appearance="primary" type="submit" disabled={update.isPending}>
                {update.isPending ? 'Saving…' : 'Save'}
              </Button>
            </DialogActions>
          </DialogBody>
        </form>
      </DialogSurface>
    </Dialog>
  )
}
