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
  Dropdown,
  Field,
  Input,
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
  Title2,
  makeStyles,
  tokens,
} from '@fluentui/react-components'
import { AddRegular, DeleteRegular, EditRegular } from '@fluentui/react-icons'
import { type ReactNode, useId, useState } from 'react'
import { ApiError } from '../../api/client'
import { useCountries, useMe } from '../../api/queries'
import { QueryError } from '../../app/QueryError'
import { canConfigureCountry } from '../../auth/roles'
import { type FieldDef, type FormValues, initialValues, missingRequired, timeZoneSuggestions, toRequestBody } from './forms'

const useStyles = makeStyles({
  page: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL, maxWidth: '1100px' },
  heading: { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: tokens.spacingHorizontalM, flexWrap: 'wrap' },
  tableWrap: { overflowX: 'auto' },
  actions: { display: 'flex', gap: tokens.spacingHorizontalXS },
  form: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
})

export interface Column<T> {
  header: string
  render: (item: T) => ReactNode
}

export interface ReferenceDataPageProps<T> {
  title: string
  description: string
  /** Lower-case singular noun used in button labels, e.g. "legal entity". */
  noun: string
  items: T[] | undefined
  isLoading: boolean
  error: unknown
  getKey: (item: T) => string
  getName: (item: T) => string
  columns: Column<T>[]
  fields: FieldDef[]
  canCreate: boolean
  canEdit: (item: T) => boolean
  onSave: (item: T | null, body: Record<string, unknown>) => Promise<unknown>
  onDelete?: (item: T) => Promise<unknown>
}

/** List + add/edit/delete dialogs for organisation reference data. The API enforces every permission. */
export function ReferenceDataPage<T extends object>(props: ReferenceDataPageProps<T>) {
  const styles = useStyles()
  const { title, description, noun, items, isLoading, error, getKey, getName, columns, canCreate, canEdit, onDelete } = props
  const [editing, setEditing] = useState<{ item: T | null } | null>(null)
  const [deleting, setDeleting] = useState<T | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const anyEditable = (items ?? []).some(canEdit)

  return (
    <section className={styles.page}>
      <div className={styles.heading}>
        <Title2 as="h1">{title}</Title2>
        {canCreate && (
          <Button
            appearance="primary"
            icon={<AddRegular />}
            onClick={() => {
              setNotice(null)
              setEditing({ item: null })
            }}
          >
            Add {noun}
          </Button>
        )}
      </div>
      <Body1>{description}</Body1>

      {notice && (
        <MessageBar intent="success">
          <MessageBarBody>{notice}</MessageBarBody>
        </MessageBar>
      )}
      {isLoading && <Spinner label={`Loading ${title.toLowerCase()}…`} />}
      {error ? <QueryError error={error} /> : null}

      {items && items.length === 0 && <Body1>None yet.</Body1>}
      {items && items.length > 0 && (
        <div className={styles.tableWrap}>
          <Table aria-label={title}>
            <TableHeader>
              <TableRow>
                {columns.map((c) => (
                  <TableHeaderCell key={c.header}>{c.header}</TableHeaderCell>
                ))}
                {anyEditable && <TableHeaderCell aria-label="Actions" />}
              </TableRow>
            </TableHeader>
            <TableBody>
              {items.map((item) => (
                <TableRow key={getKey(item)}>
                  {columns.map((c) => (
                    <TableCell key={c.header}>{c.render(item)}</TableCell>
                  ))}
                  {anyEditable && (
                    <TableCell>
                      {canEdit(item) && (
                        <div className={styles.actions}>
                          <Button
                            appearance="subtle"
                            icon={<EditRegular />}
                            aria-label={`Edit ${getName(item)}`}
                            onClick={() => {
                              setNotice(null)
                              setEditing({ item })
                            }}
                          />
                          {onDelete && (
                            <Button
                              appearance="subtle"
                              icon={<DeleteRegular />}
                              aria-label={`Delete ${getName(item)}`}
                              onClick={() => {
                                setNotice(null)
                                setDeleting(item)
                              }}
                            />
                          )}
                        </div>
                      )}
                    </TableCell>
                  )}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}

      {editing && (
        <EditDialog
          {...props}
          item={editing.item}
          onClose={(saved) => {
            setEditing(null)
            if (saved) setNotice(`${saved} saved.`)
          }}
        />
      )}
      {deleting && onDelete && (
        <DeleteDialog
          name={getName(deleting)}
          onConfirm={() => onDelete(deleting)}
          onClose={(deleted) => {
            setDeleting(null)
            if (deleted) setNotice(`${getName(deleting)} deleted.`)
          }}
        />
      )}
    </section>
  )
}

/** Whether an error is the API refusing the change, as opposed to a field validation error. */
function generalError(error: unknown): unknown {
  return error instanceof ApiError && error.problem?.errors ? undefined : error
}

function EditDialog<T extends object>({
  noun,
  fields,
  item,
  getName,
  onSave,
  onClose,
}: ReferenceDataPageProps<T> & { item: T | null; onClose: (savedName?: string) => void }) {
  const styles = useStyles()
  const mode = item ? 'edit' : 'create'
  const [values, setValues] = useState<FormValues>(() => initialValues(fields, item ?? undefined))
  const [missing, setMissing] = useState<string[]>([])
  const [error, setError] = useState<unknown>(null)
  const [saving, setSaving] = useState(false)
  const apiError = error instanceof ApiError ? error : undefined
  const set = (name: string, value: string | boolean) => setValues((v) => ({ ...v, [name]: value }))

  const submit = async () => {
    const empty = missingRequired(fields, values, mode)
    setMissing(empty)
    if (empty.length > 0) return

    setSaving(true)
    setError(null)
    try {
      await onSave(item, toRequestBody(fields, values, mode))
      onClose(String(values.name || (item ? getName(item) : noun)))
    } catch (e) {
      setError(e)
    } finally {
      setSaving(false)
    }
  }

  return (
    <Dialog open onOpenChange={(_, data) => !data.open && onClose()}>
      <DialogSurface>
        <form
          onSubmit={(e) => {
            e.preventDefault()
            void submit()
          }}
        >
          <DialogBody>
            <DialogTitle>{item ? `Edit ${getName(item)}` : `Add ${noun}`}</DialogTitle>
            <DialogContent className={styles.form}>
              {fields
                .filter((f) => !(mode === 'edit' && f.createOnly) && !(mode === 'create' && f.kind === 'active'))
                .map((field) => (
                  <FormField
                    key={field.name}
                    field={field}
                    value={values[field.name]!}
                    onChange={(v) => set(field.name, v)}
                    error={missing.includes(field.name) ? 'This field is required.' : apiError?.fieldError(field.name)}
                  />
                ))}
              {generalError(error) ? <QueryError error={error} /> : null}
            </DialogContent>
            <DialogActions>
              <Button appearance="secondary" onClick={() => onClose()}>
                Cancel
              </Button>
              <Button appearance="primary" type="submit" disabled={saving}>
                {saving ? 'Saving…' : 'Save'}
              </Button>
            </DialogActions>
          </DialogBody>
        </form>
      </DialogSurface>
    </Dialog>
  )
}

function FormField({
  field,
  value,
  onChange,
  error,
}: {
  field: FieldDef
  value: string | boolean
  onChange: (value: string | boolean) => void
  error: string | undefined
}) {
  const listId = useId()
  const text = typeof value === 'string' ? value : ''

  if (field.kind === 'active') {
    return <Switch label={field.label} checked={value === true} onChange={(_, d) => onChange(d.checked)} />
  }

  return (
    <Field label={field.label} required={field.required} hint={field.hint} validationMessage={error}>
      {field.kind === 'country' ? (
        <CountryDropdown value={text} onChange={onChange} />
      ) : field.kind === 'timeZone' ? (
        <>
          <Input value={text} onChange={(_, d) => onChange(d.value)} list={listId} placeholder="e.g. Asia/Karachi" />
          <datalist id={listId}>
            {timeZoneSuggestions().map((tz) => (
              <option key={tz} value={tz} />
            ))}
          </datalist>
        </>
      ) : (
        <Input
          value={text}
          onChange={(_, d) => onChange(d.value)}
          inputMode={field.kind === 'number' || field.kind === 'optionalNumber' ? 'numeric' : undefined}
        />
      )}
    </Field>
  )
}

/** Countries the signed-in user may configure. */
function CountryDropdown({ value, onChange }: { value: string; onChange: (value: string) => void }) {
  const { data: me } = useMe()
  const { data: countries } = useCountries()
  const options = (countries ?? []).filter((c) => canConfigureCountry(me, c.countryCode))
  const selected = options.find((c) => c.countryCode === value)

  return (
    <Dropdown
      placeholder="Select a country"
      value={selected ? `${selected.name} (${selected.countryCode})` : ''}
      selectedOptions={value ? [value] : []}
      onOptionSelect={(_, d) => onChange(d.optionValue ?? '')}
    >
      {options.map((c) => (
        <Option key={c.countryCode} value={c.countryCode}>
          {`${c.name} (${c.countryCode})`}
        </Option>
      ))}
    </Dropdown>
  )
}

function DeleteDialog({
  name,
  onConfirm,
  onClose,
}: {
  name: string
  onConfirm: () => Promise<unknown>
  onClose: (deleted?: boolean) => void
}) {
  const [error, setError] = useState<unknown>(null)
  const [deleting, setDeleting] = useState(false)

  const confirm = async () => {
    setDeleting(true)
    try {
      await onConfirm()
      onClose(true)
    } catch (e) {
      setError(e)
    } finally {
      setDeleting(false)
    }
  }

  return (
    <Dialog open onOpenChange={(_, data) => !data.open && onClose()}>
      <DialogSurface>
        <DialogBody>
          <DialogTitle>Delete {name}?</DialogTitle>
          <DialogContent>
            {error ? <QueryError error={error} /> : 'This cannot be undone. Items in use cannot be deleted; deactivate them instead.'}
          </DialogContent>
          <DialogActions>
            <Button appearance="secondary" onClick={() => onClose()}>
              Cancel
            </Button>
            <Button appearance="primary" onClick={() => void confirm()} disabled={deleting || !!error}>
              {deleting ? 'Deleting…' : 'Delete'}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  )
}

export function ActiveBadge({ active }: { active: boolean }) {
  return active ? (
    <Badge appearance="tint" color="success">
      Active
    </Badge>
  ) : (
    <Badge appearance="tint" color="subtle">
      Inactive
    </Badge>
  )
}
