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
import { CheckmarkRegular, DismissRegular, EditRegular, MailRegular, PeopleTeamRegular, SendRegular } from '@fluentui/react-icons'
import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { ApiError } from '../../api/client'
import {
  useChangeRequisitionStatus,
  useDecideRequisition,
  useRequisition,
  useSubmitRequisition,
  useWithdrawRequisition,
} from '../../api/requisitions'
import type { RequisitionDetail, RequisitionStatus } from '../../api/types'
import { useMe } from '../../api/queries'
import { QueryError } from '../../app/QueryError'
import { canUseCandidates, isHr } from '../../auth/roles'
import { SendEmailDialog } from '../templates/EmailComponents'
import { TextField } from '../employees/fields'
import { formatDate, label } from '../employees/format'
import { budgetRange, decisionColor, requisitionStatusColor, statusActionLabel } from './requisitionFormat'

const useStyles = makeStyles({
  page: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL, maxWidth: '1100px' },
  header: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalM, flexWrap: 'wrap' },
  actions: { display: 'flex', gap: tokens.spacingHorizontalS, flexWrap: 'wrap' },
  facts: { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))', gap: tokens.spacingHorizontalL, margin: 0 },
  fact: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXXS, margin: 0 },
  term: { color: tokens.colorNeutralForeground3 },
  card: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM, padding: tokens.spacingHorizontalL },
  description: { whiteSpace: 'pre-wrap' },
  tableWrap: { overflowX: 'auto' },
  form: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
})

type Pending = { kind: 'decide'; approve: boolean } | { kind: 'status'; status: RequisitionStatus } | { kind: 'withdraw' }

export function RequisitionDetailPage() {
  const styles = useStyles()
  const { id = '' } = useParams()
  const navigate = useNavigate()
  const { data: r, isLoading, error } = useRequisition(id)
  const { data: me } = useMe()
  const submit = useSubmitRequisition()
  const [pending, setPending] = useState<Pending | null>(null)
  const [emailing, setEmailing] = useState(false)

  if (isLoading) return <Spinner label="Loading…" />
  if (error || !r) return <QueryError error={error} />
  const p = r.permissions
  const round = Math.max(0, ...r.approvals.map((a) => a.round))

  return (
    <section className={styles.page}>
      <Body1>
        <Link to="/requisitions">Back to requisitions</Link>
      </Body1>
      <div className={styles.header}>
        <Title2 as="h1">
          {r.number} {r.title}
        </Title2>
        <Badge appearance="tint" color={requisitionStatusColor(r.status)}>
          {label(r.status)}
        </Badge>
      </div>

      <div className={styles.actions}>
        {p.canDecide && (
          <>
            <Button appearance="primary" icon={<CheckmarkRegular />} onClick={() => setPending({ kind: 'decide', approve: true })}>
              Approve
            </Button>
            <Button icon={<DismissRegular />} onClick={() => setPending({ kind: 'decide', approve: false })}>
              Reject
            </Button>
          </>
        )}
        {p.canEdit && (
          <Button icon={<EditRegular />} onClick={() => navigate(`/requisitions/${r.id}/edit`)}>
            Edit
          </Button>
        )}
        {p.canSubmit && (
          <Button appearance="primary" icon={<SendRegular />} disabled={submit.isPending} onClick={() => submit.mutate({ id: r.id, body: {} })}>
            Submit for approval
          </Button>
        )}
        {p.canWithdraw && <Button onClick={() => setPending({ kind: 'withdraw' })}>Withdraw</Button>}
        {isHr(me) && r.status === 'PendingApproval' && (
          <Button icon={<MailRegular />} onClick={() => setEmailing(true)}>
            Email approver
          </Button>
        )}
        {canUseCandidates(me) && r.status !== 'Draft' && r.status !== 'PendingApproval' && (
          <Button appearance="primary" icon={<PeopleTeamRegular />} onClick={() => navigate(`/requisitions/${r.id}/pipeline`)}>
            Candidates
          </Button>
        )}
        {p.statusOptions.map((s) => (
          <Button key={s} onClick={() => setPending({ kind: 'status', status: s })}>
            {statusActionLabel(s)}
          </Button>
        ))}
      </div>
      {submit.error && <QueryError error={submit.error} />}
      {emailing && (
        <SendEmailDialog
          record={{ recordType: 'Requisition', recordId: r.id, countryCode: r.countryCode, name: `the approver of ${r.number}` }}
          onClose={() => setEmailing(false)}
        />
      )}

      <dl className={styles.facts}>
        {(
          [
            ['Department', r.departmentName],
            ['Office', `${r.officeLocationName} (${r.countryCode})`],
            ['Openings', String(r.openings)],
            ['Budget (annual)', budgetRange(r.budgetMin, r.budgetMax, r.currencyCode)],
            ['Reason', r.reason === 'Replacement' ? `Replacing ${r.replacedEmployeeName}` : 'New position'],
            ['Target date', formatDate(r.targetDate)],
            ['Requested by', r.requesterName],
            ['Hiring manager', r.hiringManagerName],
            ['Interview rounds', String(r.interviewRounds)],
            ['Raised', new Date(r.createdAtUtc).toLocaleDateString()],
          ] as const
        ).map(([term, value]) => (
          <div key={term} className={styles.fact}>
            <dt className={styles.term}>
              <Caption1>{term}</Caption1>
            </dt>
            <dd className={styles.fact}>
              <Text>{value}</Text>
            </dd>
          </div>
        ))}
      </dl>

      <Card className={styles.card}>
        <Subtitle2 as="h2">Job description</Subtitle2>
        <Body1 className={styles.description}>{r.jobDescription}</Body1>
      </Card>

      {r.approvals.length > 0 && (
        <div className={styles.tableWrap}>
          <Table aria-label="Approvals">
            <TableHeader>
              <TableRow>
                <TableHeaderCell>Step</TableHeaderCell>
                <TableHeaderCell>Approver</TableHeaderCell>
                <TableHeaderCell>Decision</TableHeaderCell>
                <TableHeaderCell>Comment</TableHeaderCell>
                <TableHeaderCell>When</TableHeaderCell>
              </TableRow>
            </TableHeader>
            <TableBody>
              {r.approvals.map((a) => (
                <TableRow key={`${a.round}-${a.stepOrder}`}>
                  <TableCell>
                    {a.stepOrder}. {a.label}
                    {a.round < round && <Caption1> (earlier submission)</Caption1>}
                  </TableCell>
                  <TableCell>{a.approverName}</TableCell>
                  <TableCell>
                    <Badge appearance="tint" color={decisionColor(a.decision)}>
                      {a.decision === 'Closed' ? 'Not reached' : label(a.decision)}
                    </Badge>
                  </TableCell>
                  <TableCell>{a.comment ?? ''}</TableCell>
                  <TableCell>{a.decidedAtUtc ? new Date(a.decidedAtUtc).toLocaleString() : ''}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}

      <div className={styles.tableWrap}>
        <Table aria-label="Status history">
          <TableHeader>
            <TableRow>
              <TableHeaderCell>When</TableHeaderCell>
              <TableHeaderCell>Status</TableHeaderCell>
              <TableHeaderCell>By</TableHeaderCell>
              <TableHeaderCell>Comment</TableHeaderCell>
            </TableRow>
          </TableHeader>
          <TableBody>
            {r.history.map((h, i) => (
              <TableRow key={i}>
                <TableCell>{new Date(h.atUtc).toLocaleString()}</TableCell>
                <TableCell>{h.fromStatus ? `${label(h.fromStatus)} → ${label(h.toStatus)}` : label(h.toStatus)}</TableCell>
                <TableCell>{h.actorName}</TableCell>
                <TableCell>{h.comment ?? ''}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>

      {pending && <CommentDialog requisition={r} pending={pending} onClose={() => setPending(null)} />}
    </section>
  )
}

/** Approve, reject (comment required), withdraw or change status, with an optional comment. */
function CommentDialog({ requisition, pending, onClose }: { requisition: RequisitionDetail; pending: Pending; onClose: () => void }) {
  const styles = useStyles()
  const decide = useDecideRequisition()
  const withdraw = useWithdrawRequisition()
  const status = useChangeRequisitionStatus()
  const [comment, setComment] = useState('')
  const mutation = pending.kind === 'decide' ? decide : pending.kind === 'withdraw' ? withdraw : status
  const apiError = mutation.error instanceof ApiError ? mutation.error : undefined
  const title =
    pending.kind === 'decide'
      ? `${pending.approve ? 'Approve' : 'Reject'} ${requisition.number}`
      : pending.kind === 'withdraw'
        ? `Withdraw ${requisition.number}`
        : `${statusActionLabel(pending.status)}: ${requisition.number}`
  const required = pending.kind === 'decide' && !pending.approve

  const confirm = () => {
    const text = comment.trim() || null
    const id = requisition.id
    if (pending.kind === 'decide') decide.mutate({ id, body: { approve: pending.approve, comment: text } }, { onSuccess: onClose })
    else if (pending.kind === 'withdraw') withdraw.mutate({ id, body: { comment: text } }, { onSuccess: onClose })
    else status.mutate({ id, body: { status: pending.status, comment: text } }, { onSuccess: onClose })
  }

  return (
    <Dialog open onOpenChange={(_, d) => !d.open && onClose()}>
      <DialogSurface>
        <form
          onSubmit={(e) => {
            e.preventDefault()
            confirm()
          }}
        >
          <DialogBody>
            <DialogTitle>{title}</DialogTitle>
            <DialogContent className={styles.form}>
              <TextField
                label="Comment"
                multiline
                required={required}
                value={comment}
                onChange={setComment}
                hint={required ? 'Tell the requester why, so they can revise it.' : 'Optional; the requester sees it.'}
                error={apiError?.fieldError('comment')}
              />
              {mutation.error && !apiError?.problem?.errors ? <QueryError error={mutation.error} /> : null}
            </DialogContent>
            <DialogActions>
              <Button onClick={onClose}>Back</Button>
              <Button appearance="primary" type="submit" disabled={mutation.isPending}>
                {mutation.isPending ? 'Saving…' : 'Confirm'}
              </Button>
            </DialogActions>
          </DialogBody>
        </form>
      </DialogSurface>
    </Dialog>
  )
}
