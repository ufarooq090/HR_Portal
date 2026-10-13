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
  Label,
  Menu,
  MenuItem,
  MenuList,
  MenuPopover,
  MenuTrigger,
  MessageBar,
  MessageBarBody,
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
import { ArrowDownloadRegular, EditRegular, PersonAddRegular, ProhibitedRegular } from '@fluentui/react-icons'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { type ReactNode, useId, useRef, useState } from 'react'
import { Link, useParams } from 'react-router'
import { api, ApiError, saveFile } from '../../api/client'
import { useCountries } from '../../api/queries'
import { downloadCv, useUploadCv } from '../../api/recruiting'
import { useRequisitions } from '../../api/requisitions'
import {
  parseList,
  resumeBankKeys,
  statusLabels,
  useAddCandidateNote,
  useBlacklist,
  useMarkContacted,
  useRemoveFromBlacklist,
  useResumeBankCandidate,
  useSetResumeBankStatus,
  useUpdateResumeDetails,
} from '../../api/resumeBank'
import { type ApplicationCard, type MoneyAmount, type ResumeBankDetail, resumeBankStatuses, type ResumeBankStatus, type TimelineKind } from '../../api/types'
import { QueryError } from '../../app/QueryError'
import { SelectField, TextField } from '../employees/fields'
import { formatBytes, formatDate, formatMoney } from '../employees/format'
import { StageBadge } from '../recruiting/PipelinePage'
import { StatusBadge } from './StatusBadge'

const useStyles = makeStyles({
  page: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL, maxWidth: '1100px' },
  header: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalM, flexWrap: 'wrap' },
  actions: { display: 'flex', gap: tokens.spacingHorizontalS, flexWrap: 'wrap', alignItems: 'end' },
  card: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalS, padding: tokens.spacingHorizontalL },
  facts: { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))', gap: tokens.spacingHorizontalL, margin: 0 },
  fact: { display: 'flex', flexDirection: 'column', margin: 0 },
  term: { color: tokens.colorNeutralForeground3 },
  badges: { display: 'flex', gap: tokens.spacingHorizontalXS, flexWrap: 'wrap', alignItems: 'center' },
  form: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  timeline: { listStyle: 'none', margin: 0, padding: 0, display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  entry: { display: 'grid', gridTemplateColumns: '150px 1fr', gap: tokens.spacingHorizontalM },
  pre: { whiteSpace: 'pre-wrap' },
  tableWrap: { overflowX: 'auto' },
})

const money = (m: MoneyAmount | null) => (m ? formatMoney(m.amount, m.currency) : null)

const kindLabels: Record<TimelineKind, string> = {
  applied: 'Applied',
  stage: 'Stage',
  interview: 'Interview',
  feedback: 'Feedback',
  status: 'Status',
  note: 'Note',
  employment: 'Employment',
}

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

type Dialogs = 'status' | 'blacklist' | 'unblacklist' | 'details' | null

/** RB-02/05/06/08: one candidate's Resume Bank record, history across all applications, and HR's actions on it. */
export function ResumeBankProfilePage() {
  const styles = useStyles()
  const { personId = '' } = useParams()
  const { data: c, isLoading, error } = useResumeBankCandidate(personId)
  const contacted = useMarkContacted()
  const setStatusMutation = useSetResumeBankStatus()
  const blacklist = useBlacklist()
  const unblacklist = useRemoveFromBlacklist()
  const [dialog, setDialog] = useState<Dialogs>(null)
  const [status, setStatus] = useState<ResumeBankStatus | null>(null)

  if (isLoading) return <Spinner label="Loading…" />
  if (error || !c) return <QueryError error={error} />
  const p = c.permissions
  const blacklisted = c.status === 'Blacklisted'

  return (
    <section className={styles.page}>
      <Body1>
        <Link to="/resume-bank">Back to Resume Bank</Link>
      </Body1>
      <div className={styles.header}>
        <Title2 as="h1">{c.name}</Title2>
        {c.status && <StatusBadge status={c.status} />}
        {c.isEmployee && (
          <Badge appearance="outline" color="brand">
            Employee
          </Badge>
        )}
      </div>

      {blacklisted && (
        <MessageBar intent="error">
          <MessageBarBody>
            Blacklisted: this candidate cannot be added to requisitions.{c.blacklistReason ? ` Reason: ${c.blacklistReason}` : ''}
          </MessageBarBody>
        </MessageBar>
      )}

      {(p.canEdit || p.canBlacklist) && (
        <div className={styles.actions}>
          {p.canEdit && !blacklisted && <AddToRequisition candidate={c} />}
          {p.canEdit && !blacklisted && (
            <Menu>
              <MenuTrigger disableButtonEnhancement>
                <Button>Set status…</Button>
              </MenuTrigger>
              <MenuPopover>
                <MenuList>
                  {resumeBankStatuses
                    .filter((s) => s !== 'Blacklisted' && s !== c.status)
                    .map((s) => (
                      <MenuItem
                        key={s}
                        onClick={() => {
                          setStatus(s)
                          setDialog('status')
                        }}
                      >
                        {statusLabels[s]}
                      </MenuItem>
                    ))}
                  {c.status && (
                    <MenuItem
                      onClick={() => {
                        setStatus(null)
                        setDialog('status')
                      }}
                    >
                      Clear status
                    </MenuItem>
                  )}
                </MenuList>
              </MenuPopover>
            </Menu>
          )}
          {p.canEdit && (
            <Button icon={<EditRegular />} onClick={() => setDialog('details')}>
              Edit skills and tags
            </Button>
          )}
          {p.canEdit && (
            <Button disabled={contacted.isPending} onClick={() => contacted.mutate({ personId: c.personId, body: {} })}>
              Mark contacted today
            </Button>
          )}
          {p.canBlacklist && !blacklisted && (
            <Button icon={<ProhibitedRegular />} onClick={() => setDialog('blacklist')}>
              Blacklist
            </Button>
          )}
          {p.canBlacklist && blacklisted && <Button onClick={() => setDialog('unblacklist')}>Remove from blacklist</Button>}
        </div>
      )}
      {contacted.error && <QueryError error={contacted.error} />}

      <Card className={styles.card}>
        <Subtitle2 as="h2">Profile</Subtitle2>
        <Facts
          items={[
            ...(p.canSeeContact ? ([['Email', c.email], ['Phone', c.phone]] as [string, ReactNode][]) : []),
            ['Country', c.countryCode],
            ['Location', c.location],
            ['Current employer', c.currentEmployer],
            ['Experience', c.experienceYears !== null ? `${c.experienceYears} years` : null],
            ['Notice period', c.noticePeriodDays !== null ? `${c.noticePeriodDays} days` : null],
            ...(p.canSeeCompensation ? ([['Current CTC', money(c.currentCtc)], ['Expected CTC', money(c.expectedCtc)]] as [string, ReactNode][]) : []),
            ['Last contacted', c.lastContactedAtUtc ? formatDate(c.lastContactedAtUtc.slice(0, 10)) : null],
          ]}
        />
        {!p.canSeeContact && <Caption1>Contact details and salary are visible to HR, and salary to the hiring manager of the candidate's requisitions.</Caption1>}
        <div className={styles.badges} aria-label="Skills">
          <Caption1 className={styles.term}>Skills</Caption1>
          {c.skills.length === 0 && <Caption1>none recorded</Caption1>}
          {c.skills.map((s) => (
            <Badge key={s} appearance="outline" color="informative">
              {s}
            </Badge>
          ))}
        </div>
        <div className={styles.badges} aria-label="Tags">
          <Caption1 className={styles.term}>Tags</Caption1>
          {c.tags.length === 0 && <Caption1>none</Caption1>}
          {c.tags.map((t) => (
            <Badge key={t} appearance="tint" color="brand">
              {t}
            </Badge>
          ))}
        </div>
      </Card>

      <CvCard candidate={c} />
      <ApplicationsCard candidate={c} />
      {p.canSeeNotes && p.canEdit && <NoteCard personId={c.personId} />}
      <TimelineCard candidate={c} />

      {dialog === 'status' && (
        <ReasonDialog
          title={status ? `Set status to ${statusLabels[status]}` : 'Clear the status'}
          action="Save"
          required={false}
          mutation={setStatusMutation}
          submit={(reason, done) => setStatusMutation.mutate({ personId: c.personId, body: { status, reason: reason || null } }, { onSuccess: done })}
          onClose={() => setDialog(null)}
        />
      )}
      {dialog === 'blacklist' && (
        <ReasonDialog
          title={`Blacklist ${c.name}`}
          intro="Blacklisted candidates cannot be added to requisitions. The reason is kept and visible to HR; this is recorded in the audit log."
          action="Blacklist"
          required
          mutation={blacklist}
          submit={(reason, done) => blacklist.mutate({ personId: c.personId, body: { reason } }, { onSuccess: done })}
          onClose={() => setDialog(null)}
        />
      )}
      {dialog === 'unblacklist' && (
        <ReasonDialog
          title={`Remove ${c.name} from the blacklist`}
          action="Remove"
          required
          mutation={unblacklist}
          submit={(reason, done) => unblacklist.mutate({ personId: c.personId, body: { reason } }, { onSuccess: done })}
          onClose={() => setDialog(null)}
        />
      )}
      {dialog === 'details' && <DetailsDialog candidate={c} onClose={() => setDialog(null)} />}
    </section>
  )
}

/** RB-08: add the candidate to an open requisition in one click, reusing the Candidate Tracker's add. */
function AddToRequisition({ candidate }: { candidate: ResumeBankDetail }) {
  const styles = useStyles()
  const queryClient = useQueryClient()
  const { data: requisitions } = useRequisitions(['Open'])
  const [requisitionId, setRequisitionId] = useState('')
  const add = useMutation({
    mutationFn: (id: string) =>
      api.post<ApplicationCard>(`/api/requisitions/${encodeURIComponent(id)}/applications`, { personId: candidate.personId, candidate: null, source: 'Portal' }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: resumeBankKeys.detail(candidate.personId) }),
  })
  const already = new Set(candidate.applications.map((a) => a.requisitionNumber))
  const options = (requisitions ?? []).filter((r) => !already.has(r.number)).map((r) => ({ value: r.id, label: `${r.number} ${r.title} (${r.countryCode})` }))

  return (
    <div className={styles.actions}>
      <SelectField label="Open requisition" value={requisitionId} onChange={setRequisitionId} options={options} placeholder="Choose a requisition" />
      <Button appearance="primary" icon={<PersonAddRegular />} disabled={!requisitionId || add.isPending} onClick={() => add.mutate(requisitionId, { onSuccess: () => setRequisitionId('') })}>
        Add to requisition
      </Button>
      {add.data && (
        <Body1 role="status">
          Added. <Link to={`/applications/${add.data.id}`}>Open the application</Link>
        </Body1>
      )}
      {add.error && <QueryError error={add.error} />}
    </div>
  )
}

function CvCard({ candidate: c }: { candidate: ResumeBankDetail }) {
  const styles = useStyles()
  const queryClient = useQueryClient()
  const upload = useUploadCv()
  const inputId = useId()
  const input = useRef<HTMLInputElement>(null)
  const [error, setError] = useState<unknown>(null)

  return (
    <Card className={styles.card}>
      <Subtitle2 as="h2">CVs</Subtitle2>
      {c.cvs.length === 0 && <Body1>No CV uploaded.</Body1>}
      {c.cvs.map((cv) => (
        <div key={cv.id} className={styles.actions}>
          <Button appearance="subtle" icon={<ArrowDownloadRegular />} onClick={() => downloadCv(c.personId, cv.id).then(saveFile, setError)}>
            {cv.fileName}
          </Button>
          <Caption1>
            {formatBytes(cv.sizeBytes)} · {cv.uploadedBy} · {new Date(cv.uploadedAtUtc).toLocaleDateString()}
          </Caption1>
        </div>
      ))}
      {c.permissions.canEdit && (
        <div className={styles.actions}>
          <Label htmlFor={inputId}>Upload CV</Label>
          <input
            id={inputId}
            ref={input}
            type="file"
            accept=".pdf,.doc,.docx"
            onChange={(e) => {
              const file = e.target.files?.[0]
              if (file)
                upload.mutate(
                  { personId: c.personId, file },
                  {
                    onSuccess: () => queryClient.invalidateQueries({ queryKey: resumeBankKeys.detail(c.personId) }),
                    onSettled: () => input.current && (input.current.value = ''),
                  },
                )
            }}
          />
          {upload.isPending && <Spinner size="tiny" />}
          <Caption1>PDF and Word (.docx) CVs are searchable; scanned PDFs and old .doc files are stored but not searchable.</Caption1>
        </div>
      )}
      {upload.error || error ? <QueryError error={upload.error ?? error} /> : null}
    </Card>
  )
}

function ApplicationsCard({ candidate: c }: { candidate: ResumeBankDetail }) {
  const styles = useStyles()
  return (
    <Card className={styles.card}>
      <Subtitle2 as="h2">Applications</Subtitle2>
      {c.applications.length === 0 ? (
        <Body1>Not in any requisition yet.</Body1>
      ) : (
        <div className={styles.tableWrap}>
          <Table aria-label="Applications" size="small">
            <TableHeader>
              <TableRow>
                <TableHeaderCell>Requisition</TableHeaderCell>
                <TableHeaderCell>Outcome</TableHeaderCell>
                <TableHeaderCell>Applied</TableHeaderCell>
              </TableRow>
            </TableHeader>
            <TableBody>
              {c.applications.map((a) => (
                <TableRow key={a.applicationId}>
                  <TableCell>
                    {a.canOpen ? (
                      <Link to={`/applications/${a.applicationId}`}>
                        {a.requisitionNumber} {a.requisitionTitle}
                      </Link>
                    ) : (
                      `${a.requisitionNumber} ${a.requisitionTitle}`
                    )}{' '}
                    ({a.countryCode})
                  </TableCell>
                  <TableCell>
                    <StageBadge stage={a.stage} round={a.interviewRound} />
                  </TableCell>
                  <TableCell>{formatDate(a.appliedAtUtc.slice(0, 10))}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </Card>
  )
}

function NoteCard({ personId }: { personId: string }) {
  const styles = useStyles()
  const add = useAddCandidateNote()
  const [body, setBody] = useState('')
  return (
    <Card className={styles.card}>
      <Subtitle2 as="h2">Add a note</Subtitle2>
      <form
        className={styles.form}
        onSubmit={(e) => {
          e.preventDefault()
          add.mutate({ personId, body: { body: body.trim() } }, { onSuccess: () => setBody('') })
        }}
      >
        <TextField label="Note" multiline value={body} onChange={setBody} hint="Visible to HR only." />
        <div>
          <Button type="submit" disabled={!body.trim() || add.isPending}>
            Add note
          </Button>
        </div>
        {add.error && <QueryError error={add.error} />}
      </form>
    </Card>
  )
}

/** RB-05: everything that happened with the candidate, newest first. */
function TimelineCard({ candidate: c }: { candidate: ResumeBankDetail }) {
  const styles = useStyles()
  return (
    <Card className={styles.card}>
      <Subtitle2 as="h2">History</Subtitle2>
      {c.timeline.length === 0 && <Body1>Nothing yet.</Body1>}
      <ol className={styles.timeline} aria-label="History">
        {c.timeline.map((t, i) => (
          <li key={i} className={styles.entry}>
            <div>
              <Badge appearance="outline" color="subtle">
                {kindLabels[t.kind]}
              </Badge>
              <br />
              <Caption1>{new Date(t.atUtc).toLocaleString([], { dateStyle: 'medium', timeStyle: 'short' })}</Caption1>
            </div>
            <div>
              <Text weight="semibold">{t.title}</Text>
              {t.detail && <Body1 className={styles.pre}>{t.detail}</Body1>}
            </div>
          </li>
        ))}
      </ol>
    </Card>
  )
}

function ReasonDialog({
  title,
  intro,
  action,
  required,
  mutation: save,
  submit,
  onClose,
}: {
  title: string
  intro?: string
  action: string
  required: boolean
  mutation: { isPending: boolean; error: Error | null }
  submit: (reason: string, done: () => void) => void
  onClose: () => void
}) {
  const styles = useStyles()
  const [reason, setReason] = useState('')
  const apiError = save.error instanceof ApiError ? save.error : undefined
  return (
    <Dialog open onOpenChange={(_, d) => !d.open && onClose()}>
      <DialogSurface>
        <form
          onSubmit={(e) => {
            e.preventDefault()
            submit(reason.trim(), onClose)
          }}
        >
          <DialogBody>
            <DialogTitle>{title}</DialogTitle>
            <DialogContent className={styles.form}>
              {intro && <Body1>{intro}</Body1>}
              <TextField label="Reason" multiline required={required} value={reason} onChange={setReason} error={apiError?.fieldError('reason')} />
              {save.error && !apiError?.fieldError('reason') ? <QueryError error={save.error} /> : null}
            </DialogContent>
            <DialogActions>
              <Button onClick={onClose}>Cancel</Button>
              <Button appearance="primary" type="submit" disabled={save.isPending || (required && !reason.trim())}>
                {action}
              </Button>
            </DialogActions>
          </DialogBody>
        </form>
      </DialogSurface>
    </Dialog>
  )
}

function DetailsDialog({ candidate: c, onClose }: { candidate: ResumeBankDetail; onClose: () => void }) {
  const styles = useStyles()
  const save = useUpdateResumeDetails()
  const { data: countries } = useCountries()
  const [country, setCountry] = useState(c.countryCode)
  const [location, setLocation] = useState(c.location ?? '')
  const [experience, setExperience] = useState(c.experienceYears?.toString() ?? '')
  const [skills, setSkills] = useState(c.skills.join(', '))
  const [tags, setTags] = useState(c.tags.join(', '))
  const apiError = save.error instanceof ApiError ? save.error : undefined
  return (
    <Dialog open onOpenChange={(_, d) => !d.open && onClose()}>
      <DialogSurface>
        <form
          onSubmit={(e) => {
            e.preventDefault()
            save.mutate(
              {
                personId: c.personId,
                body: {
                  countryCode: country,
                  location: location.trim() || null,
                  experienceYears: experience.trim() === '' ? null : Number(experience),
                  skills: parseList(skills),
                  tags: parseList(tags),
                },
              },
              { onSuccess: onClose },
            )
          }}
        >
          <DialogBody>
            <DialogTitle>Skills, tags and location</DialogTitle>
            <DialogContent className={styles.form}>
              <SelectField
                label="Country"
                required
                value={country}
                onChange={setCountry}
                options={(countries ?? []).map((x) => ({ value: x.countryCode, label: x.name }))}
                hint="HR in this country manage the candidate."
              />
              <TextField label="Location" value={location} onChange={setLocation} error={apiError?.fieldError('location')} />
              <TextField label="Experience (years)" type="number" value={experience} onChange={setExperience} error={apiError?.fieldError('experienceYears')} />
              <TextField label="Skills" value={skills} onChange={setSkills} hint="Comma-separated, e.g. Python, SQL, Spark" error={apiError?.fieldError('skills')} />
              <TextField label="Tags" value={tags} onChange={setTags} hint="Comma-separated, e.g. good for future data roles" error={apiError?.fieldError('tags')} />
              {save.error && !apiError?.problem?.errors ? <QueryError error={save.error} /> : null}
            </DialogContent>
            <DialogActions>
              <Button onClick={onClose}>Cancel</Button>
              <Button appearance="primary" type="submit" disabled={save.isPending}>
                Save
              </Button>
            </DialogActions>
          </DialogBody>
        </form>
      </DialogSurface>
    </Dialog>
  )
}
