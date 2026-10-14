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
  Dropdown,
  Field,
  Input,
  Label,
  Menu,
  MenuItem,
  MenuList,
  MenuPopover,
  MenuTrigger,
  Option,
  Spinner,
  Subtitle2,
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
import { ArrowDownloadRegular, ArrowUploadRegular, CalendarAddRegular, EditRegular, EyeOffRegular, MailRegular } from '@fluentui/react-icons'
import { type ReactNode, useId, useRef, useState } from 'react'
import { Link, useParams } from 'react-router'
import { ApiError, saveFile } from '../../api/client'
import { useEmployees } from '../../api/employees'
import { useCountries } from '../../api/queries'
import {
  downloadCv,
  useAddNote,
  useApplication,
  useCancelInterview,
  useScheduleInterview,
  useSubmitScorecard,
  useUpdateCandidate,
  useUploadCv,
} from '../../api/recruiting'
import type { ApplicationDetail, InterviewMode, InterviewView, MoneyAmount, Recommendation } from '../../api/types'
import { useMe } from '../../api/queries'
import { QueryError } from '../../app/QueryError'
import { canUseTemplates, isHr } from '../../auth/roles'
import { EmailHistoryCard, SendEmailDialog } from '../templates/EmailComponents'
import { DateField, SelectField, TextField } from '../employees/fields'
import { formatBytes, formatDateTime, formatDay, formatMoney, label } from '../employees/format'
import { moveTargets, stageLabel, type StageTarget } from './pipeline'
import { StageBadge } from './PipelinePage'
import { MoveDialog } from './RecruitingDialogs'

const useStyles = makeStyles({
  page: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL, maxWidth: '1100px' },
  header: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalM, flexWrap: 'wrap' },
  actions: { display: 'flex', gap: tokens.spacingHorizontalS, flexWrap: 'wrap', alignItems: 'center' },
  card: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalS, padding: tokens.spacingHorizontalL },
  facts: { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))', gap: tokens.spacingHorizontalL, margin: 0 },
  fact: { display: 'flex', flexDirection: 'column', margin: 0 },
  term: { color: tokens.colorNeutralForeground3 },
  muted: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS, color: tokens.colorNeutralForeground3 },
  form: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  ratings: { display: 'grid', gridTemplateColumns: 'minmax(160px, 1fr) 120px minmax(160px, 2fr)', gap: tokens.spacingHorizontalM, alignItems: 'end' },
  tableWrap: { overflowX: 'auto' },
  pre: { whiteSpace: 'pre-wrap' },
})

function Facts({ items }: { items: [string, ReactNode][] }) {
  const styles = useStyles()
  return (
    <dl className={styles.facts}>
      {items.map(([term, value]) => (
        <div key={term} className={styles.fact}>
          <dt className={styles.term}>
            <Caption1>{term}</Caption1>
          </dt>
          <dd className={styles.fact}>
            <Text>{value ?? '—'}</Text>
          </dd>
        </div>
      ))}
    </dl>
  )
}

const money = (m: MoneyAmount | null) => (m ? formatMoney(m.amount, m.currency) : null)

/** One application: the candidate, their interviews and feedback, notes and stage history, per the user's permissions. */
export function ApplicationPage() {
  const styles = useStyles()
  const { id = '' } = useParams()
  const { data: a, isLoading, error } = useApplication(id)
  const [moving, setMoving] = useState<StageTarget | null>(null)
  const [scheduling, setScheduling] = useState(false)
  const [editing, setEditing] = useState(false)
  const [emailing, setEmailing] = useState(false)
  const { data: me } = useMe()

  if (isLoading) return <Spinner label="Loading…" />
  if (error || !a) return <QueryError error={error} />
  const p = a.permissions
  const c = a.candidate
  // HR, and the hiring manager for interview emails (the server enforces which templates).
  const canEmail = canUseTemplates(me) && (p.canManage || p.canSeeNotes)

  return (
    <section className={styles.page}>
      <Body1>
        <Link to={`/requisitions/${a.requisition.id}/pipeline`}>
          Pipeline: {a.requisition.number} {a.requisition.title}
        </Link>
      </Body1>
      <div className={styles.header}>
        <Title2 as="h1">{c.name}</Title2>
        <StageBadge stage={a.stage} round={a.interviewRound} />
      </div>
      {canEmail && !p.canManage && (
        <div className={styles.actions}>
          <Button icon={<MailRegular />} onClick={() => setEmailing(true)}>
            Send email
          </Button>
        </div>
      )}
      {p.canManage && (
        <div className={styles.actions}>
          <Button icon={<MailRegular />} onClick={() => setEmailing(true)}>
            Send email
          </Button>
          <Menu>
            <MenuTrigger disableButtonEnhancement>
              <Button appearance="primary">Move to…</Button>
            </MenuTrigger>
            <MenuPopover>
              <MenuList>
                {moveTargets({ stage: a.stage, interviewRound: a.interviewRound }, a.requisition.interviewRounds).map((t) => (
                  <MenuItem key={t.key} onClick={() => setMoving(t)}>
                    {t.label}
                  </MenuItem>
                ))}
              </MenuList>
            </MenuPopover>
          </Menu>
          <Button icon={<CalendarAddRegular />} onClick={() => setScheduling(true)}>
            Add interview
          </Button>
          <Button icon={<EditRegular />} onClick={() => setEditing(true)}>
            Edit candidate
          </Button>
        </div>
      )}

      <Card className={styles.card}>
        <Facts
          items={[
            ['Email', c.email],
            ['Phone', c.phone],
            ['Current employer', c.currentEmployer],
            ['Notice period', c.noticePeriodDays != null ? `${c.noticePeriodDays} days` : null],
            ['Source', label(a.source)],
            ['Applied', formatDay(a.appliedAtUtc)],
            ...(p.canSeeCompensation
              ? ([
                  ['Current CTC', money(c.currentCtc)],
                  ['Expected CTC', money(c.expectedCtc)],
                ] as [string, ReactNode][])
              : []),
          ]}
        />
      </Card>

      <CvSection detail={a} />

      <Subtitle2 as="h2">Interviews</Subtitle2>
      {a.interviews.length === 0 && <Body1>No interviews yet.</Body1>}
      {a.interviews.map((i) => (
        <InterviewCard key={i.id} detail={a} interview={i} />
      ))}

      {p.canSeeNotes && <NotesSection detail={a} />}

      {p.canManage && isHr(me) && <EmailHistoryCard person={{ personId: c.personId }} />}

      {a.history.length > 0 && (
        <div className={styles.tableWrap}>
          <Table aria-label="Stage history">
            <TableHeader>
              <TableRow>
                <TableHeaderCell>When</TableHeaderCell>
                <TableHeaderCell>Change</TableHeaderCell>
                <TableHeaderCell>Reason</TableHeaderCell>
                <TableHeaderCell>By</TableHeaderCell>
              </TableRow>
            </TableHeader>
            <TableBody>
              {a.history.map((h, i) => (
                <TableRow key={i}>
                  <TableCell>{formatDateTime(h.atUtc)}</TableCell>
                  <TableCell>
                    {h.fromStage ? `${stageLabel(h.fromStage, h.fromRound)} → ` : ''}
                    {stageLabel(h.toStage, h.toRound)}
                  </TableCell>
                  <TableCell>{[h.rejectionReason, h.reason].filter(Boolean).join(': ')}</TableCell>
                  <TableCell>{h.actorName}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}

      {moving && <MoveDialog applicationId={a.id} candidateName={c.name} target={moving} onClose={() => setMoving(null)} />}
      {scheduling && <ScheduleDialog detail={a} onClose={() => setScheduling(false)} />}
      {editing && <EditCandidateDialog detail={a} onClose={() => setEditing(false)} />}
      {emailing && (
        <SendEmailDialog
          record={{ recordType: 'Application', recordId: a.id, name: c.name, historyKey: c.personId }}
          onClose={() => setEmailing(false)}
        />
      )}
    </section>
  )
}

function CvSection({ detail }: { detail: ApplicationDetail }) {
  const styles = useStyles()
  const upload = useUploadCv()
  const inputId = useId()
  const input = useRef<HTMLInputElement>(null)
  const [error, setError] = useState<unknown>(null)

  return (
    <Card className={styles.card}>
      <Subtitle2 as="h2">CV</Subtitle2>
      {detail.cvs.length === 0 && <Body1>No CV uploaded.</Body1>}
      {detail.cvs.map((cv) => (
        <div key={cv.id} className={styles.actions}>
          <Button
            appearance="subtle"
            icon={<ArrowDownloadRegular />}
            onClick={() => downloadCv(detail.candidate.personId, cv.id).then(saveFile, setError)}
          >
            {cv.fileName}
          </Button>
          <Caption1>
            {formatBytes(cv.sizeBytes)} · {cv.uploadedBy} · {formatDay(cv.uploadedAtUtc)}
          </Caption1>
        </div>
      ))}
      {detail.permissions.canManage && (
        <div className={styles.actions}>
          <Label htmlFor={inputId}>Upload CV</Label>
          <input
            id={inputId}
            ref={input}
            type="file"
            accept=".pdf,.doc,.docx"
            onChange={(e) => {
              const file = e.target.files?.[0]
              if (file) upload.mutate({ personId: detail.candidate.personId, file }, { onSettled: () => input.current && (input.current.value = '') })
            }}
          />
          {upload.isPending && <Spinner size="tiny" />}
          <ArrowUploadRegular aria-hidden />
        </div>
      )}
      {(upload.error || error) ? <QueryError error={upload.error ?? error} /> : null}
    </Card>
  )
}

/** CT-06/07: an interview with the scorecards the viewer may see. */
function InterviewCard({ detail, interview: i }: { detail: ApplicationDetail; interview: InterviewView }) {
  const styles = useStyles()
  const cancel = useCancelInterview()
  const [scoring, setScoring] = useState(false)
  const cancelled = i.status === 'Cancelled'

  return (
    <Card className={styles.card} aria-label={`Interview round ${i.round}`}>
      <div className={styles.actions}>
        <Text weight="semibold">Round {i.round}</Text>
        <Text>
          {formatDateTime(i.startUtc)} · {i.durationMinutes} min · {label(i.mode)}
          {i.location ? ` · ${i.location}` : ''}
        </Text>
        {cancelled && (
          <Badge appearance="tint" color="subtle">
            Cancelled
          </Badge>
        )}
      </div>
      <Caption1>Panel: {i.panelNames.join(', ')}</Caption1>
      {!cancelled && (
        <div className={styles.actions}>
          {i.isOnPanel && !i.myScorecardSubmitted && (
            <Button appearance="primary" onClick={() => setScoring(true)}>
              Submit feedback
            </Button>
          )}
          {detail.permissions.canManage && (
            <Button disabled={cancel.isPending} onClick={() => cancel.mutate({ id: detail.id, interviewId: i.id })}>
              Cancel interview
            </Button>
          )}
        </div>
      )}
      {i.feedbackHidden && (
        <Body1 className={styles.muted}>
          <EyeOffRegular /> {i.submittedCount} of the panel have submitted. You will see their feedback once you submit yours.
        </Body1>
      )}
      {i.scorecards.map((s) => (
        <Card key={s.interviewerName} appearance="outline" className={styles.card} aria-label={`Feedback from ${s.interviewerName}`}>
          <div className={styles.actions}>
            <Text weight="semibold">{s.interviewerName}</Text>
            <Badge appearance="tint" color={s.recommendation === 'NoHire' ? 'danger' : 'success'}>
              {label(s.recommendation)}
            </Badge>
          </div>
          {s.ratings.map((r) => (
            <Caption1 key={r.criterion}>
              {r.criterion}: {r.rating}/5{r.comment ? ` — ${r.comment}` : ''}
            </Caption1>
          ))}
          {s.comment && <Body1 className={styles.pre}>{s.comment}</Body1>}
        </Card>
      ))}
      {scoring && <ScorecardDialog interview={i} onClose={() => setScoring(false)} />}
    </Card>
  )
}

const recommendations: Recommendation[] = ['StrongHire', 'Hire', 'NoHire']

function ScorecardDialog({ interview, onClose }: { interview: InterviewView; onClose: () => void }) {
  const styles = useStyles()
  const submit = useSubmitScorecard()
  const [ratings, setRatings] = useState(() => interview.criteria.map((criterion) => ({ criterion, rating: '', comment: '' })))
  const [recommendation, setRecommendation] = useState('')
  const [comment, setComment] = useState('')
  const apiError = submit.error instanceof ApiError ? submit.error : undefined
  const complete = recommendation && ratings.every((r) => r.rating)

  return (
    <Dialog open onOpenChange={(_, d) => !d.open && onClose()}>
      <DialogSurface>
        <form
          onSubmit={(e) => {
            e.preventDefault()
            submit.mutate(
              {
                interviewId: interview.id,
                ratings: ratings.map((r) => ({ criterion: r.criterion, rating: Number(r.rating), comment: r.comment.trim() || null })),
                recommendation: recommendation as Recommendation,
                comment: comment.trim() || null,
              },
              { onSuccess: onClose },
            )
          }}
        >
          <DialogBody>
            <DialogTitle>Feedback for round {interview.round}</DialogTitle>
            <DialogContent className={styles.form}>
              <Caption1>Rate each criterion from 1 (poor) to 5 (excellent). Feedback cannot be changed after you submit.</Caption1>
              {ratings.map((r, index) => (
                <div key={r.criterion} className={styles.ratings}>
                  <Text>{r.criterion}</Text>
                  <SelectField
                    label={`${r.criterion} rating`}
                    required
                    value={r.rating}
                    onChange={(v) => setRatings((all) => all.map((x, j) => (j === index ? { ...x, rating: v } : x)))}
                    options={['1', '2', '3', '4', '5'].map((n) => ({ value: n, label: n }))}
                  />
                  <TextField
                    label={`${r.criterion} comment`}
                    value={r.comment}
                    onChange={(v) => setRatings((all) => all.map((x, j) => (j === index ? { ...x, comment: v } : x)))}
                  />
                </div>
              ))}
              <SelectField
                label="Recommendation"
                required
                value={recommendation}
                onChange={setRecommendation}
                options={recommendations.map((x) => ({ value: x, label: label(x) }))}
              />
              <TextField label="Overall comments" multiline value={comment} onChange={setComment} />
              {submit.error ? <QueryError error={apiError?.problem?.errors ? new ApiError(400, { title: apiError.fieldError('ratings') ?? apiError.message }) : submit.error} /> : null}
            </DialogContent>
            <DialogActions>
              <Button onClick={onClose}>Cancel</Button>
              <Button appearance="primary" type="submit" disabled={!complete || submit.isPending}>
                Submit feedback
              </Button>
            </DialogActions>
          </DialogBody>
        </form>
      </DialogSurface>
    </Dialog>
  )
}

/** Recorded in the portal; ICalendarService creates the event (only logged locally until Outlook invites in Phase 2). */
function ScheduleDialog({ detail, onClose }: { detail: ApplicationDetail; onClose: () => void }) {
  const styles = useStyles()
  const schedule = useScheduleInterview()
  const { data: people } = useEmployees({ status: ['Probation', 'Confirmed', 'OnNotice'] })
  const [round, setRound] = useState(String(detail.interviewRound ?? 1))
  const [day, setDay] = useState('')
  const [time, setTime] = useState('')
  const [duration, setDuration] = useState('60')
  const [mode, setMode] = useState<InterviewMode>('Teams')
  const [location, setLocation] = useState('')
  const [panel, setPanel] = useState<string[]>([])
  const apiError = schedule.error instanceof ApiError ? schedule.error : undefined
  const names = new Map((people ?? []).map((p) => [p.id, `${p.fullName} (${p.employeeNumber})`]))

  return (
    <Dialog open onOpenChange={(_, d) => !d.open && onClose()}>
      <DialogSurface>
        <form
          onSubmit={(e) => {
            e.preventDefault()
            schedule.mutate(
              {
                id: detail.id,
                body: {
                  round: Number(round),
                  startUtc: new Date(`${day}T${time}`).toISOString(),
                  durationMinutes: Number(duration) || 0,
                  mode,
                  location: location.trim() || null,
                  panelEmployeeIds: panel,
                },
              },
              { onSuccess: onClose },
            )
          }}
        >
          <DialogBody>
            <DialogTitle>Add interview</DialogTitle>
            <DialogContent className={styles.form}>
              <SelectField
                label="Round"
                required
                value={round}
                onChange={setRound}
                options={Array.from({ length: detail.requisition.interviewRounds }, (_, i) => ({ value: String(i + 1), label: `Round ${i + 1}` }))}
              />
              <DateField label="Date" required value={day} onChange={setDay} />
              <Field label="Time" required hint="24-hour clock, in your time zone.">
                <Input type="time" value={time} onChange={(_, d) => setTime(d.value)} required />
              </Field>
              <TextField label="Duration (minutes)" type="number" required value={duration} onChange={setDuration} error={apiError?.fieldError('durationMinutes')} />
              <SelectField
                label="Mode"
                required
                value={mode}
                onChange={(v) => setMode(v as InterviewMode)}
                options={(['Teams', 'InPerson', 'Phone'] as const).map((m) => ({ value: m, label: label(m) }))}
              />
              <TextField label="Location or link" value={location} onChange={setLocation} />
              <Field label="Panel" required validationMessage={apiError?.fieldError('panelEmployeeIds')}>
                <Dropdown
                  multiselect
                  placeholder="Choose interviewers"
                  selectedOptions={panel}
                  value={panel.map((id) => names.get(id) ?? id).join(', ')}
                  onOptionSelect={(_, d) => setPanel(d.selectedOptions)}
                >
                  {(people ?? []).map((p) => (
                    <Option key={p.id} value={p.id}>
                      {`${p.fullName} (${p.employeeNumber})`}
                    </Option>
                  ))}
                </Dropdown>
              </Field>
              {schedule.error && !apiError?.problem?.errors ? <QueryError error={schedule.error} /> : null}
            </DialogContent>
            <DialogActions>
              <Button onClick={onClose}>Cancel</Button>
              <Button appearance="primary" type="submit" disabled={schedule.isPending || panel.length === 0 || !day || !time}>
                Save interview
              </Button>
            </DialogActions>
          </DialogBody>
        </form>
      </DialogSurface>
    </Dialog>
  )
}

/** CT-08: free-text notes with author and time. */
function NotesSection({ detail }: { detail: ApplicationDetail }) {
  const styles = useStyles()
  const add = useAddNote()
  const [body, setBody] = useState('')

  return (
    <Card className={styles.card}>
      <Subtitle2 as="h2">Notes</Subtitle2>
      <form
        className={styles.form}
        onSubmit={(e) => {
          e.preventDefault()
          if (body.trim()) add.mutate({ id: detail.id, body: { body } }, { onSuccess: () => setBody('') })
        }}
      >
        <TextField label="Add a note" multiline value={body} onChange={setBody} hint="Visible to HR and the hiring manager, not to interviewers." />
        <div>
          <Button type="submit" disabled={!body.trim() || add.isPending}>
            Add note
          </Button>
        </div>
      </form>
      {add.error && <QueryError error={add.error} />}
      {detail.notes.map((n) => (
        <div key={n.id} aria-label={`Note by ${n.authorName}`}>
          <Caption1>
            {n.authorName} · {formatDateTime(n.createdAtUtc)}
          </Caption1>
          <Body1 className={styles.pre}>{n.body}</Body1>
        </div>
      ))}
    </Card>
  )
}

function EditCandidateDialog({ detail, onClose }: { detail: ApplicationDetail; onClose: () => void }) {
  const styles = useStyles()
  const update = useUpdateCandidate()
  const { data: countries } = useCountries()
  const c = detail.candidate
  const [given, ...rest] = c.name.split(' ')
  const [form, setForm] = useState({
    givenName: given ?? '',
    familyName: rest.join(' '),
    email: c.email ?? '',
    phone: c.phone ?? '',
    currentEmployer: c.currentEmployer ?? '',
    noticePeriodDays: c.noticePeriodDays?.toString() ?? '',
    currentCtc: c.currentCtc?.amount.toString() ?? '',
    expectedCtc: c.expectedCtc?.amount.toString() ?? '',
  })
  const [currency, setCurrency] = useState(c.expectedCtc?.currency ?? c.currentCtc?.currency ?? 'PKR')
  const apiError = update.error instanceof ApiError ? update.error : undefined
  const set = (key: keyof typeof form, value: string) => setForm((f) => ({ ...f, [key]: value }))
  const amount = (text: string) => (text.trim() ? { amount: Number(text.replace(/,/g, '')) || 0, currency } : null)

  return (
    <Dialog open onOpenChange={(_, d) => !d.open && onClose()}>
      <DialogSurface>
        <form
          onSubmit={(e) => {
            e.preventDefault()
            update.mutate(
              {
                personId: c.personId,
                body: {
                  givenName: form.givenName,
                  familyName: form.familyName,
                  email: form.email.trim() || null,
                  phone: form.phone.trim() || null,
                  currentEmployer: form.currentEmployer.trim() || null,
                  noticePeriodDays: form.noticePeriodDays.trim() ? Number.parseInt(form.noticePeriodDays, 10) : null,
                  currentCtc: amount(form.currentCtc),
                  expectedCtc: amount(form.expectedCtc),
                },
              },
              { onSuccess: onClose },
            )
          }}
        >
          <DialogBody>
            <DialogTitle>Edit {c.name}</DialogTitle>
            <DialogContent className={styles.form}>
              <TextField label="Given name" required value={form.givenName} onChange={(v) => set('givenName', v)} />
              <TextField label="Family name" required value={form.familyName} onChange={(v) => set('familyName', v)} />
              <TextField label="Email" type="email" value={form.email} onChange={(v) => set('email', v)} error={apiError?.fieldError('email')} />
              <TextField label="Phone" type="tel" value={form.phone} onChange={(v) => set('phone', v)} />
              <TextField label="Current employer" value={form.currentEmployer} onChange={(v) => set('currentEmployer', v)} />
              <TextField label="Notice period (days)" type="number" value={form.noticePeriodDays} onChange={(v) => set('noticePeriodDays', v)} />
              <SelectField label="Salary currency" value={currency} onChange={setCurrency} options={[...new Set((countries ?? []).map((x) => x.currencyCode))].map((x) => ({ value: x, label: x }))} />
              <TextField label={`Current CTC (${currency})`} value={form.currentCtc} onChange={(v) => set('currentCtc', v)} />
              <TextField label={`Expected CTC (${currency})`} value={form.expectedCtc} onChange={(v) => set('expectedCtc', v)} />
              {update.error && !apiError?.problem?.errors ? <QueryError error={update.error} /> : null}
            </DialogContent>
            <DialogActions>
              <Button onClick={onClose}>Cancel</Button>
              <Button appearance="primary" type="submit" disabled={update.isPending}>
                Save
              </Button>
            </DialogActions>
          </DialogBody>
        </form>
      </DialogSurface>
    </Dialog>
  )
}
