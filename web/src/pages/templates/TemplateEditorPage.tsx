import {
  Badge,
  Body1,
  Button,
  Caption1,
  Card,
  Checkbox,
  Combobox,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Field,
  Input,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Option,
  Spinner,
  Subtitle2,
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
import { SaveRegular } from '@fluentui/react-icons'
import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { ApiError } from '../../api/client'
import { useEmployees } from '../../api/employees'
import { useCountries, useMe } from '../../api/queries'
import { useCandidates } from '../../api/recruiting'
import { useRequisitions } from '../../api/requisitions'
import {
  type EmailPreview,
  previewEmail,
  recordTypeLabels,
  type SenderMailbox,
  senderLabels,
  templateCategories,
  type TemplateCategory,
  type TemplateDetail,
  templateRecordTypes,
  type TemplateRecordType,
  type TemplateRequest,
  unknownFields,
  useMergeFields,
  useSaveTemplate,
  useSendTest,
  useTemplate,
  useTemplateVersion,
} from '../../api/templates'
import { QueryError } from '../../app/QueryError'
import { RichTextEditor } from '../../app/RichTextEditor'
import { canManageTemplates } from '../../auth/roles'
import { SelectField } from '../employees/fields'
import { EmailBody } from './EmailComponents'

const useStyles = makeStyles({
  page: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL, maxWidth: '1000px' },
  card: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM, padding: tokens.spacingHorizontalL },
  grid: { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))', gap: tokens.spacingHorizontalM, alignItems: 'end' },
  row: { display: 'flex', gap: tokens.spacingHorizontalM, flexWrap: 'wrap', alignItems: 'end' },
  header: { display: 'flex', gap: tokens.spacingHorizontalM, alignItems: 'center', flexWrap: 'wrap' },
  tableWrap: { overflowX: 'auto' },
  form: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
})

const empty: TemplateRequest = {
  name: '',
  category: 'General',
  recordType: 'Application',
  subject: '',
  bodyHtml: '',
  countryCode: null,
  defaultSender: 'HrMailbox',
  ccManager: false,
  isActive: true,
}

const toRequest = (t: TemplateDetail): TemplateRequest => ({
  name: t.name,
  category: t.category,
  recordType: t.recordType,
  subject: t.subject,
  bodyHtml: t.bodyHtml,
  countryCode: t.countryCode,
  defaultSender: t.defaultSender,
  ccManager: t.ccManager,
  isActive: t.isActive,
})

/** ET-01/02/06: create or edit a template (HR Admins); others see it read-only. */
export function TemplateEditorPage() {
  const { id = '' } = useParams()
  const { data, isLoading, error } = useTemplate(id)
  if (id && isLoading) return <Spinner label="Loading…" />
  if (id && (error || !data)) return <QueryError error={error} />
  // Keyed so the form starts again from the saved template after each save.
  return <TemplateForm key={data ? `${data.id}-${data.version}` : 'new'} template={data ?? null} />
}

function TemplateForm({ template }: { template: TemplateDetail | null }) {
  const styles = useStyles()
  const navigate = useNavigate()
  const { data: me } = useMe()
  const { data: countries } = useCountries()
  const canEdit = canManageTemplates(me) && (template?.canManage ?? true)
  const [form, setForm] = useState<TemplateRequest>(template ? toRequest(template) : empty)
  const { data: fields } = useMergeFields(form.recordType)
  const save = useSaveTemplate(template?.id ?? null)
  const [viewing, setViewing] = useState<number | null>(null)
  const set = <K extends keyof TemplateRequest>(key: K, value: TemplateRequest[K]) => setForm((f) => ({ ...f, [key]: value }))
  const apiError = save.error instanceof ApiError ? save.error : undefined
  const unknown = fields ? unknownFields(`${form.subject} ${form.bodyHtml}`, fields) : []

  return (
    <section className={styles.page}>
      <Body1>
        <Link to="/templates">Back to email templates</Link>
      </Body1>
      <div className={styles.header}>
        <Title2 as="h1">{template ? template.name : 'New template'}</Title2>
        {template && <Badge appearance="outline">Version {template.version}</Badge>}
        {template?.isRestricted && (
          <Badge appearance="outline" color="danger">
            HR Admin only, never in bulk
          </Badge>
        )}
      </div>
      {template?.trigger && (
        <Caption1>
          Sent automatically in a later phase when: {template.trigger}. For now it is sent by hand.
        </Caption1>
      )}

      {canEdit ? (
        <form
          className={styles.form}
          onSubmit={(e) => {
            e.preventDefault()
            save.mutate(form, { onSuccess: (saved) => navigate(`/templates/${saved.id}`, { replace: true }) })
          }}
        >
          <Card className={styles.card}>
            <div className={styles.grid}>
              <Field label="Name" required validationMessage={apiError?.fieldError('name')}>
                <Input value={form.name} onChange={(_, d) => set('name', d.value)} />
              </Field>
              <SelectField
                label="Category"
                value={form.category}
                onChange={(v) => set('category', v as TemplateCategory)}
                options={templateCategories.map((c) => ({ value: c, label: c }))}
              />
              <SelectField
                label="Sent to"
                value={form.recordType}
                onChange={(v) => setForm((f) => ({ ...f, recordType: v as TemplateRecordType, ccManager: v === 'Employee' && f.ccManager }))}
                options={templateRecordTypes.map((t) => ({ value: t, label: recordTypeLabels[t] }))}
                hint={template ? 'Fixed once the template exists.' : 'Decides who receives it and which fields can be used.'}
                error={apiError?.fieldError('recordType')}
              />
              <SelectField
                label="Country"
                value={form.countryCode ?? ''}
                onChange={(v) => set('countryCode', v || null)}
                clearLabel="Both countries"
                options={(countries ?? []).map((c) => ({ value: c.countryCode, label: c.name }))}
              />
              <SelectField
                label="Default sender"
                value={form.defaultSender}
                onChange={(v) => set('defaultSender', v as SenderMailbox)}
                options={(['HrMailbox', 'CurrentUser'] as const).map((s) => ({ value: s, label: senderLabels[s] }))}
                hint="HR can change it before sending."
              />
            </div>
            <div className={styles.row}>
              <Switch label="Active" checked={form.isActive} onChange={(_, d) => set('isActive', d.checked)} />
              {form.recordType === 'Employee' && <Checkbox label="Copy the employee's manager" checked={form.ccManager} onChange={(_, d) => set('ccManager', !!d.checked)} />}
            </div>
            <Field label="Subject" required hint="Merge fields work here too, e.g. {{designation}}." validationMessage={apiError?.fieldError('subject')}>
              <Input value={form.subject} onChange={(_, d) => set('subject', d.value)} />
            </Field>
            <Field label="Body" required validationMessage={apiError?.fieldError('bodyHtml')}>
              <RichTextEditor label="Body" value={form.bodyHtml} onChange={(html) => set('bodyHtml', html)} mergeFields={fields} />
            </Field>
            {unknown.length > 0 && (
              <MessageBar intent="warning">
                <MessageBarBody>
                  These fields are not available for {recordTypeLabels[form.recordType].toLowerCase()} emails: {unknown.map((f) => `{{${f}}}`).join(', ')}.
                </MessageBarBody>
              </MessageBar>
            )}
            {save.error && !apiError?.problem?.errors ? <QueryError error={save.error} /> : null}
            <div className={styles.row}>
              <Button type="submit" appearance="primary" icon={<SaveRegular />} disabled={save.isPending || unknown.length > 0}>
                {template ? 'Save new version' : 'Create template'}
              </Button>
            </div>
          </Card>
        </form>
      ) : (
        template && (
          <Card className={styles.card}>
            <Body1>
              <strong>Subject:</strong> {template.subject}
            </Body1>
            <EmailBody html={template.bodyHtml} label="Template body" />
          </Card>
        )
      )}

      {template && <PreviewCard template={template} subject={form.subject} body={form.bodyHtml} />}

      {template && canEdit && (
        <Card className={styles.card}>
          <Subtitle2 as="h2">Version history</Subtitle2>
          <div className={styles.tableWrap}>
            <Table aria-label="Version history" size="small">
              <TableHeader>
                <TableRow>
                  <TableHeaderCell>Version</TableHeaderCell>
                  <TableHeaderCell>Saved by</TableHeaderCell>
                  <TableHeaderCell>When</TableHeaderCell>
                  <TableHeaderCell />
                </TableRow>
              </TableHeader>
              <TableBody>
                {template.versions.map((v) => (
                  <TableRow key={v.version}>
                    <TableCell>{v.version}</TableCell>
                    <TableCell>{v.changedByName}</TableCell>
                    <TableCell>{new Date(v.atUtc).toLocaleString()}</TableCell>
                    <TableCell>
                      <Button size="small" onClick={() => setViewing(v.version)}>
                        View version {v.version}
                      </Button>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        </Card>
      )}
      {template && viewing !== null && <VersionDialog templateId={template.id} version={viewing} onClose={() => setViewing(null)} />}
    </section>
  )
}

function VersionDialog({ templateId, version, onClose }: { templateId: string; version: number; onClose: () => void }) {
  const styles = useStyles()
  const { data, error } = useTemplateVersion(templateId, version)
  return (
    <Dialog open onOpenChange={(_, d) => !d.open && onClose()}>
      <DialogSurface>
        <DialogBody>
          <DialogTitle>Version {version}</DialogTitle>
          <DialogContent className={styles.form}>
            {error && <QueryError error={error} />}
            {!data && !error && <Spinner size="small" />}
            {data && (
              <>
                <Caption1>
                  {data.name} · {data.category} · {data.isActive ? 'Active' : 'Inactive'} · saved by {data.changedByName} on {new Date(data.atUtc).toLocaleString()}
                </Caption1>
                <Body1>
                  <strong>Subject:</strong> {data.subject}
                </Body1>
                <EmailBody html={data.bodyHtml} label={`Version ${version} body`} />
              </>
            )}
          </DialogContent>
          <DialogActions>
            <Button appearance="primary" onClick={onClose}>
              Close
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  )
}

/** ET-03: the template (including unsaved edits) filled with a real record's data, and a test to yourself. */
function PreviewCard({ template, subject, body }: { template: TemplateDetail; subject: string; body: string }) {
  const styles = useStyles()
  const [recordId, setRecordId] = useState('')
  const [preview, setPreview] = useState<EmailPreview | null>(null)
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)
  const test = useSendTest()
  const draft = { templateId: template.id, recordId, subject: subject || null, bodyHtml: body || null }

  return (
    <Card className={styles.card}>
      <Subtitle2 as="h2">Preview with real data</Subtitle2>
      <div className={styles.row}>
        <RecordPicker recordType={template.recordType} value={recordId} onChange={setRecordId} />
        <Button
          disabled={!recordId || busy}
          onClick={async () => {
            setBusy(true)
            setError(null)
            try {
              setPreview(await previewEmail(draft))
            } catch (e) {
              setError(e)
              setPreview(null)
            } finally {
              setBusy(false)
            }
          }}
        >
          Preview
        </Button>
        <Button disabled={!recordId || test.isPending} onClick={() => test.mutate({ ...draft, subject, bodyHtml: body, sender: template.defaultSender, attachmentIds: [] })}>
          Send test to myself
        </Button>
      </div>
      {error ? <QueryError error={error} /> : null}
      {test.data && (
        <MessageBar intent="info">
          <MessageBarBody>Test sent to {test.data.sentTo}.</MessageBarBody>
        </MessageBar>
      )}
      {test.error && <QueryError error={test.error} />}
      {preview && (
        <>
          {!preview.canSend && (
            <MessageBar intent="warning">
              <MessageBarBody>
                <MessageBarTitle>Sending to {preview.recipientName} would be blocked</MessageBarTitle>
                {preview.missingFields.length > 0 && <>These merge fields have no value: {preview.missingFields.map((f) => f.label).join(', ')}. </>}
                {preview.problems.join(' ')}
              </MessageBarBody>
            </MessageBar>
          )}
          <Body1>
            <strong>To:</strong> {preview.to ?? '(no address)'}
            {preview.cc.length > 0 && ` · Cc: ${preview.cc.join(', ')}`}
          </Body1>
          <Body1>
            <strong>Subject:</strong> {preview.subject}
          </Body1>
          <EmailBody html={preview.bodyHtml} label="Preview" />
        </>
      )}
    </Card>
  )
}

/** Picks the record to preview with: an application, an employee or a requisition awaiting approval. */
function RecordPicker({ recordType, value, onChange }: { recordType: TemplateRecordType; value: string; onChange: (id: string) => void }) {
  const candidates = useCandidates({})
  const employees = useEmployees({})
  const requisitions = useRequisitions(['PendingApproval'])
  const options =
    recordType === 'Application'
      ? (candidates.data ?? []).map((a) => ({ id: a.id, label: `${a.candidateName} – ${a.requisitionNumber} ${a.requisitionTitle}` }))
      : recordType === 'Employee'
        ? (employees.data ?? []).map((e) => ({ id: e.id, label: `${e.fullName} (${e.employeeNumber})` }))
        : (requisitions.data ?? []).map((r) => ({ id: r.id, label: `${r.number} ${r.title}` }))
  const selected = options.find((o) => o.id === value)
  const [query, setQuery] = useState('')
  const shown = options.filter((o) => o.label.toLowerCase().includes(query.trim().toLowerCase())).slice(0, 50)

  return (
    <Field label={`${recordTypeLabels[recordType]} to preview with`}>
      <Combobox
        placeholder="Type to search"
        value={selected && !query ? selected.label : query}
        selectedOptions={value ? [value] : []}
        onChange={(e) => setQuery(e.target.value)}
        onOptionSelect={(_, d) => {
          onChange(d.optionValue ?? '')
          setQuery('')
        }}
      >
        {shown.map((o) => (
          <Option key={o.id} value={o.id}>
            {o.label}
          </Option>
        ))}
      </Combobox>
    </Field>
  )
}
