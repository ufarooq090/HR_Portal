import {
  Badge,
  Body1,
  Button,
  Caption1,
  Card,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Field,
  Input,
  Label,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Spinner,
  Subtitle2,
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableHeaderCell,
  TableRow,
  makeStyles,
  tokens,
} from '@fluentui/react-components'
import { AttachRegular, DismissRegular, MailRegular, SendRegular } from '@fluentui/react-icons'
import { useEffect, useId, useRef, useState } from 'react'
import {
  type Attachment,
  type BulkResult,
  type EmailPreview,
  type SenderMailbox,
  previewEmail,
  senderLabels,
  type TemplateRecordType,
  uploadAttachment,
  useEmailHistory,
  useMergeFields,
  useSendBulk,
  useSendEmail,
  useSendTest,
  useTemplates,
} from '../../api/templates'
import { QueryError } from '../../app/QueryError'
import { RichTextEditor } from '../../app/RichTextEditor'
import { SelectField } from '../employees/fields'
import { formatBytes, formatDateTime } from '../employees/format'

const useStyles = makeStyles({
  wide: { maxWidth: '900px', width: '100%' },
  form: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  row: { display: 'flex', gap: tokens.spacingHorizontalM, flexWrap: 'wrap', alignItems: 'end' },
  card: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalS, padding: tokens.spacingHorizontalL },
  body: {
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
    padding: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground1,
    overflowWrap: 'anywhere',
  },
  tableWrap: { overflowX: 'auto' },
  muted: { color: tokens.colorNeutralForeground3 },
  subject: { justifyContent: 'flex-start', textAlign: 'left', paddingLeft: 0, minWidth: 0 },
})

/** Email HTML as stored by the server, which allows only formatting, lists and safe links. */
export function EmailBody({ html, label }: { html: string; label: string }) {
  const styles = useStyles()
  // eslint-disable-next-line react/no-danger -- sanitised to a strict allowlist on the server (EmailHtml)
  return <div className={styles.body} role="document" aria-label={label} dangerouslySetInnerHTML={{ __html: html }} />
}

function AttachmentPicker({ value, onChange }: { value: Attachment[]; onChange: (value: Attachment[]) => void }) {
  const styles = useStyles()
  const inputId = useId()
  const input = useRef<HTMLInputElement>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<unknown>(null)
  return (
    <div className={styles.form}>
      <div className={styles.row}>
        <Label htmlFor={inputId}>Attachments</Label>
        <input
          id={inputId}
          ref={input}
          type="file"
          accept=".pdf,.doc,.docx,.jpg,.jpeg,.png"
          disabled={busy || value.length >= 5}
          onChange={async (e) => {
            const file = e.target.files?.[0]
            if (!file) return
            setBusy(true)
            setError(null)
            try {
              onChange([...value, await uploadAttachment(file)])
            } catch (err) {
              setError(err)
            } finally {
              setBusy(false)
              if (input.current) input.current.value = ''
            }
          }}
        />
        {busy && <Spinner size="tiny" />}
      </div>
      {value.map((a) => (
        <div key={a.id} className={styles.row}>
          <AttachRegular aria-hidden />
          <Body1>{a.fileName}</Body1>
          <Caption1>{formatBytes(a.sizeBytes)}</Caption1>
          <Button size="small" appearance="subtle" icon={<DismissRegular />} aria-label={`Remove ${a.fileName}`} onClick={() => onChange(value.filter((x) => x.id !== a.id))} />
        </div>
      ))}
      <Caption1>PDF, Word, JPG or PNG, up to 10 MB each and 5 files.</Caption1>
      {error ? <QueryError error={error} /> : null}
    </div>
  )
}

/** Why the email cannot go yet: merge fields without a value and other problems (ET-02). */
function Blockers({ preview }: { preview: EmailPreview }) {
  if (preview.canSend) return null
  return (
    <MessageBar intent="warning">
      <MessageBarBody>
        <MessageBarTitle>This email cannot be sent yet</MessageBarTitle>
        {preview.missingFields.length > 0 && <>These merge fields have no value: {preview.missingFields.map((f) => f.label).join(', ')}. </>}
        {preview.problems.join(' ')}
      </MessageBarBody>
    </MessageBar>
  )
}

export interface EmailRecordRef {
  recordType: TemplateRecordType
  recordId: string
  /** Narrows the template list; the server checks the country either way. */
  countryCode?: string
  /** Shown in the title. */
  name: string
  /** The person or employee id whose email history to refresh after sending. */
  historyKey?: string
}

/**
 * ET-02..04: pick a template, see it filled with the record's data, edit it, attach files, send a test to yourself,
 * then send. The server checks everything again.
 */
export function SendEmailDialog({ record, onClose }: { record: EmailRecordRef; onClose: () => void }) {
  const styles = useStyles()
  const { data: templates } = useTemplates({ recordType: record.recordType, country: record.countryCode, activeOnly: true })
  const { data: fields } = useMergeFields(record.recordType)
  const [templateId, setTemplateId] = useState('')
  const [preview, setPreview] = useState<EmailPreview | null>(null)
  const [previewError, setPreviewError] = useState<unknown>(null)
  const [subject, setSubject] = useState('')
  const [body, setBody] = useState('')
  const [edited, setEdited] = useState(false)
  const [sender, setSender] = useState<SenderMailbox>('HrMailbox')
  const [attachments, setAttachments] = useState<Attachment[]>([])
  const send = useSendEmail(record.historyKey)
  const test = useSendTest()

  // Fill the chosen template with the record's data.
  useEffect(() => {
    if (!templateId) return
    let cancelled = false
    previewEmail({ templateId, recordId: record.recordId, subject: null, bodyHtml: null }).then(
      (p) => {
        if (cancelled) return
        setPreview(p)
        setSubject(p.subject)
        setBody(p.bodyHtml)
        setSender(p.defaultSender)
        setEdited(false)
      },
      (e) => !cancelled && setPreviewError(e),
    )
    return () => {
      cancelled = true
    }
  }, [templateId, record.recordId])

  // After HR edits, check again what is still missing (without replacing their text).
  useEffect(() => {
    if (!templateId || !edited) return
    const timer = setTimeout(() => {
      previewEmail({ templateId, recordId: record.recordId, subject, bodyHtml: body }).then(
        (p) => setPreview((current) => (current ? { ...current, canSend: p.canSend, missingFields: p.missingFields, problems: p.problems } : p)),
        setPreviewError,
      )
    }, 600)
    return () => clearTimeout(timer)
  }, [templateId, record.recordId, subject, body, edited])

  const request = { templateId, recordId: record.recordId, subject, bodyHtml: body, sender, attachmentIds: attachments.map((a) => a.id) }

  return (
    <Dialog open onOpenChange={(_, d) => !d.open && onClose()}>
      <DialogSurface className={styles.wide}>
        <DialogBody>
          <DialogTitle>Email {record.name}</DialogTitle>
          <DialogContent className={styles.form}>
            {send.data ? (
              <MessageBar intent={send.data.status === 'Sent' ? 'success' : 'error'}>
                <MessageBarBody>
                  {send.data.status === 'Sent' ? `Email sent to ${send.data.to}.` : `The email could not be sent: ${send.data.error}`} It is logged on the record.
                </MessageBarBody>
              </MessageBar>
            ) : (
              <>
                <SelectField
                  label="Template"
                  value={templateId}
                  onChange={(id) => {
                    setPreview(null)
                    setPreviewError(null)
                    setTemplateId(id)
                  }}
                  placeholder="Choose a template"
                  options={(templates ?? []).map((t) => ({ value: t.id, label: `${t.name} (${t.category})` }))}
                />
                {templates && templates.length === 0 && <Body1>No active templates for this kind of record.</Body1>}
                {templateId && !preview && !previewError && <Spinner size="small" label="Filling in the template…" />}
                {previewError ? <QueryError error={previewError} /> : null}
                {preview && (
                  <>
                    <Body1>
                      <strong>To:</strong> {preview.to ?? '(no address)'}
                      {preview.cc.length > 0 && (
                        <>
                          {' '}
                          · <strong>Cc:</strong> {preview.cc.join(', ')}
                        </>
                      )}
                    </Body1>
                    <Blockers preview={preview} />
                    <div className={styles.row}>
                      <SelectField
                        label="Send from"
                        value={sender}
                        onChange={(v) => setSender(v as SenderMailbox)}
                        options={(['HrMailbox', 'CurrentUser'] as const).map((s) => ({ value: s, label: senderLabels[s] }))}
                      />
                    </div>
                    <Field label="Subject">
                      <Input
                        value={subject}
                        onChange={(_, d) => {
                          setSubject(d.value)
                          setEdited(true)
                        }}
                      />
                    </Field>
                    <Field label="Message" hint="Edit freely before sending; fields still shown as {{field}} must be filled in or removed.">
                      <RichTextEditor
                        label="Message"
                        value={body}
                        mergeFields={fields}
                        onChange={(html) => {
                          setBody(html)
                          setEdited(true)
                        }}
                      />
                    </Field>
                    <AttachmentPicker value={attachments} onChange={setAttachments} />
                    {test.data && (
                      <MessageBar intent="info">
                        <MessageBarBody>Test sent to {test.data.sentTo}.</MessageBarBody>
                      </MessageBar>
                    )}
                    {test.error && <QueryError error={test.error} />}
                    {send.error && <QueryError error={send.error} />}
                  </>
                )}
              </>
            )}
          </DialogContent>
          <DialogActions>
            {send.data ? (
              <Button appearance="primary" onClick={onClose}>
                Close
              </Button>
            ) : (
              <>
                <Button onClick={onClose}>Cancel</Button>
                <Button icon={<MailRegular />} disabled={!preview || test.isPending} onClick={() => test.mutate(request)}>
                  Send test to myself
                </Button>
                <Button appearance="primary" icon={<SendRegular />} disabled={!preview?.canSend || send.isPending} onClick={() => send.mutate(request)}>
                  Send
                </Button>
              </>
            )}
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  )
}

/** ET-05: one template to many candidates, each personalised; then a summary of what was sent and what failed. */
export function BulkEmailDialog({ applicationIds, onClose }: { applicationIds: string[]; onClose: () => void }) {
  const styles = useStyles()
  const { data: templates } = useTemplates({ recordType: 'Application', activeOnly: true })
  const bulk = useSendBulk()
  const [templateId, setTemplateId] = useState('')
  const [sender, setSender] = useState<SenderMailbox>('HrMailbox')
  const [attachments, setAttachments] = useState<Attachment[]>([])
  const usable = (templates ?? []).filter((t) => !t.isRestricted)

  return (
    <Dialog open onOpenChange={(_, d) => !d.open && onClose()}>
      <DialogSurface className={styles.wide}>
        <DialogBody>
          <DialogTitle>Email {applicationIds.length === 1 ? '1 candidate' : `${applicationIds.length} candidates`}</DialogTitle>
          <DialogContent className={styles.form}>
            {bulk.data ? (
              <BulkSummary result={bulk.data} />
            ) : (
              <>
                <Body1>Each candidate gets their own copy, filled with their details. Anyone whose email cannot be filled in completely is skipped and listed.</Body1>
                <SelectField
                  label="Template"
                  value={templateId}
                  onChange={(id) => {
                    setTemplateId(id)
                    const t = usable.find((x) => x.id === id)
                    if (t) setSender(t.defaultSender)
                  }}
                  placeholder="Choose a template"
                  options={usable.map((t) => ({ value: t.id, label: `${t.name} (${t.category})` }))}
                />
                <SelectField
                  label="Send from"
                  value={sender}
                  onChange={(v) => setSender(v as SenderMailbox)}
                  options={(['HrMailbox', 'CurrentUser'] as const).map((s) => ({ value: s, label: senderLabels[s] }))}
                />
                <AttachmentPicker value={attachments} onChange={setAttachments} />
                {bulk.error && <QueryError error={bulk.error} />}
              </>
            )}
          </DialogContent>
          <DialogActions>
            {bulk.data ? (
              <Button appearance="primary" onClick={onClose}>
                Close
              </Button>
            ) : (
              <>
                <Button onClick={onClose}>Cancel</Button>
                <Button
                  appearance="primary"
                  icon={<SendRegular />}
                  disabled={!templateId || bulk.isPending}
                  onClick={() => bulk.mutate({ templateId, recordIds: applicationIds, sender, attachmentIds: attachments.map((a) => a.id) })}
                >
                  {bulk.isPending ? 'Sending…' : `Send ${applicationIds.length}`}
                </Button>
              </>
            )}
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  )
}

function BulkSummary({ result }: { result: BulkResult }) {
  const styles = useStyles()
  return (
    <>
      <MessageBar intent={result.failed.length === 0 ? 'success' : 'warning'}>
        <MessageBarBody>
          <MessageBarTitle>
            Sent {result.sent.length}, not sent {result.failed.length}.
          </MessageBarTitle>
          Every email sent is logged on the candidate's record.
        </MessageBarBody>
      </MessageBar>
      <div className={styles.tableWrap}>
        <Table aria-label="Bulk send results" size="small">
          <TableHeader>
            <TableRow>
              <TableHeaderCell>Candidate</TableHeaderCell>
              <TableHeaderCell>Email</TableHeaderCell>
              <TableHeaderCell>Result</TableHeaderCell>
            </TableRow>
          </TableHeader>
          <TableBody>
            {[...result.failed, ...result.sent].map((item) => (
              <TableRow key={item.recordId}>
                <TableCell>{item.name}</TableCell>
                <TableCell>{item.to ?? '—'}</TableCell>
                <TableCell>
                  {item.error ? (
                    <>
                      <Badge appearance="tint" color="danger">
                        Not sent
                      </Badge>{' '}
                      {item.error}
                    </>
                  ) : (
                    <Badge appearance="tint" color="success">
                      Sent
                    </Badge>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
    </>
  )
}

/** ET-08: every email sent to the person, as candidate and as employee. */
export function EmailHistoryCard({ person }: { person: { personId: string } | { employeeId: string } }) {
  const styles = useStyles()
  const { data, isLoading, error } = useEmailHistory(person)
  const [open, setOpen] = useState<string | null>(null)
  const shown = data?.find((e) => e.id === open)
  return (
    <Card className={styles.card}>
      <Subtitle2 as="h2">Emails</Subtitle2>
      {isLoading && <Spinner size="small" />}
      {error && <QueryError error={error} />}
      {data && data.length === 0 && <Body1>No emails sent from the portal yet.</Body1>}
      {data && data.length > 0 && (
        <div className={styles.tableWrap}>
          <Table aria-label="Emails sent" size="small">
            <TableHeader>
              <TableRow>
                <TableHeaderCell>Sent</TableHeaderCell>
                <TableHeaderCell>Subject</TableHeaderCell>
                <TableHeaderCell>Template</TableHeaderCell>
                <TableHeaderCell>To</TableHeaderCell>
                <TableHeaderCell>By</TableHeaderCell>
                <TableHeaderCell>Status</TableHeaderCell>
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.map((e) => (
                <TableRow key={e.id}>
                  <TableCell>{formatDateTime(e.sentAtUtc)}</TableCell>
                  <TableCell>
                    <Button appearance="transparent" className={styles.subject} onClick={() => setOpen(e.id)}>
                      {e.subject}
                    </Button>
                  </TableCell>
                  <TableCell>{e.templateName ? `${e.templateName} (v${e.templateVersion})` : '—'}</TableCell>
                  <TableCell>{e.to}</TableCell>
                  <TableCell>{e.sentByName}</TableCell>
                  <TableCell>
                    <Badge appearance="tint" color={e.status === 'Sent' ? 'success' : 'danger'}>
                      {e.status}
                    </Badge>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
      {shown && (
        <Dialog open onOpenChange={(_, d) => !d.open && setOpen(null)}>
          <DialogSurface className={styles.wide}>
            <DialogBody>
              <DialogTitle>{shown.subject}</DialogTitle>
              <DialogContent className={styles.form}>
                <Caption1 className={styles.muted}>
                  {formatDateTime(shown.sentAtUtc)} · from {shown.fromAddress} to {shown.to}
                  {shown.cc.length > 0 ? `, cc ${shown.cc.join(', ')}` : ''} · by {shown.sentByName}
                </Caption1>
                {shown.error && (
                  <MessageBar intent="error">
                    <MessageBarBody>{shown.error}</MessageBarBody>
                  </MessageBar>
                )}
                <EmailBody html={shown.bodyHtml} label="Email message" />
                {shown.attachments.length > 0 && <Body1>Attachments: {shown.attachments.join(', ')}</Body1>}
              </DialogContent>
              <DialogActions>
                <Button appearance="primary" onClick={() => setOpen(null)}>
                  Close
                </Button>
              </DialogActions>
            </DialogBody>
          </DialogSurface>
        </Dialog>
      )}
    </Card>
  )
}
