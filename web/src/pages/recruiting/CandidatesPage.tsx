import {
  Body1,
  Button,
  Checkbox,
  SearchBox,
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
import { MailRegular } from '@fluentui/react-icons'
import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { useCandidates, useMyInterviews } from '../../api/recruiting'
import { type CandidateSource, candidateSources, exitStages, type PipelineStage, pipelineStages } from '../../api/types'
import { useMe } from '../../api/queries'
import { QueryError } from '../../app/QueryError'
import { isHr } from '../../auth/roles'
import { BulkEmailDialog } from '../templates/EmailComponents'
import { SelectField } from '../employees/fields'
import { formatDate, label } from '../employees/format'
import { daysSince, stageLabel } from './pipeline'
import { StageBadge } from './PipelinePage'

const useStyles = makeStyles({
  page: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL, maxWidth: '1280px' },
  filters: { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))', gap: tokens.spacingHorizontalM, alignItems: 'end' },
  tableWrap: { overflowX: 'auto' },
})

/** CT-01 list view across requisitions: every application the user may see (HR, hiring managers, panelists). */
export function CandidatesPage() {
  const styles = useStyles()
  const [input, setInput] = useState('')
  const [search, setSearch] = useState('')
  const [stage, setStage] = useState('')
  const [source, setSource] = useState('')
  const { data, isLoading, error } = useCandidates({ search, stage: (stage || undefined) as PipelineStage | undefined, source: (source || undefined) as CandidateSource | undefined })
  const { data: me } = useMe()
  const canBulk = isHr(me)
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [emailing, setEmailing] = useState(false)
  const visible = data?.filter((a) => selected.has(a.id)).map((a) => a.id) ?? []
  const toggle = (id: string, on: boolean) =>
    setSelected((s) => {
      const next = new Set(s)
      if (on) next.add(id)
      else next.delete(id)
      return next
    })

  useEffect(() => {
    const timer = setTimeout(() => setSearch(input.trim()), 250)
    return () => clearTimeout(timer)
  }, [input])

  return (
    <section className={styles.page}>
      <Title2 as="h1">Candidates</Title2>
      <Body1>HR sees candidates in their countries, hiring managers their own requisitions, and interviewers the candidates they interview.</Body1>
      <div className={styles.filters}>
        <SearchBox aria-label="Search candidates" placeholder="Name, email or employer" value={input} onChange={(_, d) => setInput(d.value)} />
        <SelectField
          label="Stage"
          value={stage}
          onChange={setStage}
          clearLabel="All stages"
          options={[...pipelineStages, ...exitStages].map((s) => ({ value: s, label: stageLabel(s) }))}
        />
        <SelectField label="Source" value={source} onChange={setSource} clearLabel="All sources" options={candidateSources.map((s) => ({ value: s, label: label(s) }))} />
      </div>
      {isLoading && <Spinner label="Loading candidates…" />}
      {error && <QueryError error={error} />}
      {data && data.length === 0 && <Body1>No candidates.</Body1>}
      {canBulk && data && data.length > 0 && (
        <div>
          <Button icon={<MailRegular />} disabled={visible.length === 0} onClick={() => setEmailing(true)}>
            Email selected ({visible.length})
          </Button>
        </div>
      )}
      {emailing && <BulkEmailDialog applicationIds={visible} onClose={() => setEmailing(false)} />}
      {data && data.length > 0 && (
        <div className={styles.tableWrap}>
          <Table aria-label="Candidates">
            <TableHeader>
              <TableRow>
                {canBulk && (
                  <TableHeaderCell>
                    <Checkbox
                      aria-label="Select all candidates"
                      checked={visible.length === 0 ? false : visible.length === data.length ? true : 'mixed'}
                      onChange={(_, d) => setSelected(d.checked ? new Set(data.map((a) => a.id)) : new Set())}
                    />
                  </TableHeaderCell>
                )}
                <TableHeaderCell>Candidate</TableHeaderCell>
                <TableHeaderCell>Requisition</TableHeaderCell>
                <TableHeaderCell>Stage</TableHeaderCell>
                <TableHeaderCell>Source</TableHeaderCell>
                <TableHeaderCell>Current employer</TableHeaderCell>
                <TableHeaderCell>Days in stage</TableHeaderCell>
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.map((a) => (
                <TableRow key={a.id}>
                  {canBulk && (
                    <TableCell>
                      <Checkbox aria-label={`Select ${a.candidateName}`} checked={selected.has(a.id)} onChange={(_, d) => toggle(a.id, !!d.checked)} />
                    </TableCell>
                  )}
                  <TableCell>
                    <Link to={`/applications/${a.id}`}>{a.candidateName}</Link>
                  </TableCell>
                  <TableCell>
                    {a.requisitionNumber} {a.requisitionTitle} ({a.countryCode})
                  </TableCell>
                  <TableCell>
                    <StageBadge stage={a.stage} round={a.interviewRound} />
                  </TableCell>
                  <TableCell>{label(a.source)}</TableCell>
                  <TableCell>{a.currentEmployer ?? '—'}</TableCell>
                  <TableCell>{daysSince(a.stageChangedAtUtc)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </section>
  )
}

/** Interviews the signed-in user sits on, with whether their feedback is in. */
export function MyInterviewsPage() {
  const styles = useStyles()
  const { data, isLoading, error } = useMyInterviews()

  return (
    <section className={styles.page}>
      <Title2 as="h1">My interviews</Title2>
      {isLoading && <Spinner label="Loading…" />}
      {error && <QueryError error={error} />}
      {data && data.length === 0 && <Body1>You are not on any interview panels.</Body1>}
      {data && data.length > 0 && (
        <div className={styles.tableWrap}>
          <Table aria-label="My interviews">
            <TableHeader>
              <TableRow>
                <TableHeaderCell>Candidate</TableHeaderCell>
                <TableHeaderCell>Requisition</TableHeaderCell>
                <TableHeaderCell>Round</TableHeaderCell>
                <TableHeaderCell>When</TableHeaderCell>
                <TableHeaderCell>Mode</TableHeaderCell>
                <TableHeaderCell>Feedback</TableHeaderCell>
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.map((i) => (
                <TableRow key={i.interviewId}>
                  <TableCell>
                    <Link to={`/applications/${i.applicationId}`}>{i.candidateName}</Link>
                  </TableCell>
                  <TableCell>
                    {i.requisitionNumber} {i.requisitionTitle}
                  </TableCell>
                  <TableCell>{i.round}</TableCell>
                  <TableCell>
                    {formatDate(i.startUtc.slice(0, 10))} {new Date(i.startUtc).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                  </TableCell>
                  <TableCell>
                    {label(i.mode)}
                    {i.location ? ` · ${i.location}` : ''}
                  </TableCell>
                  <TableCell>
                    {i.scorecardSubmitted ? (
                      'Submitted'
                    ) : (
                      <Link to={`/applications/${i.applicationId}`}>
                        <Button size="small" appearance="primary">
                          Give feedback
                        </Button>
                      </Link>
                    )}
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
