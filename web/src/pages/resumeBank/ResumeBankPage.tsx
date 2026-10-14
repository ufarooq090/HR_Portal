import {
  Badge,
  Body1,
  Button,
  Caption1,
  SearchBox,
  Spinner,
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableHeaderCell,
  TableRow,
  Title2,
  Tooltip,
  makeStyles,
  tokens,
} from '@fluentui/react-components'
import { ArrowUploadRegular, DocumentRegular } from '@fluentui/react-icons'
import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { useCountries, useMe } from '../../api/queries'
import { type ResumeBankFilters, statusLabels, useResumeBankSearch } from '../../api/resumeBank'
import { resumeBankStatuses, type ResumeBankStatus } from '../../api/types'
import { fieldGrid, fieldGridActions, tableWrap, truncate } from '../../app/layout'
import { QueryError } from '../../app/QueryError'
import { canImportCandidates, hasAnyRole, Roles } from '../../auth/roles'
import { DateField, SelectField, TextField } from '../employees/fields'
import { formatDay, formatMoney } from '../employees/format'
import { StatusBadge } from './StatusBadge'

const useStyles = makeStyles({
  page: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL, maxWidth: '1400px' },
  header: { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: tokens.spacingHorizontalM, flexWrap: 'wrap' },
  filters: fieldGrid,
  search: { gridColumn: '1 / -1', maxWidth: '640px' },
  actions: fieldGridActions,
  tableWrap,
  // Fluent tables split the width equally. These widths keep tags and outcomes on one line; below their total the
  // table scrolls inside its container.
  colCandidate: { width: '200px' },
  colStatus: { width: '130px' },
  colSkills: { width: '240px' },
  colShort: { width: '90px' },
  colLocation: { width: '120px' },
  colMoney: { width: '130px' },
  colOutcome: { width: '200px' },
  colDate: { width: '115px' },
  truncate,
  candidate: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalXXS, minWidth: 0 },
  skills: { display: 'flex', gap: tokens.spacingHorizontalXS, overflow: 'hidden', minWidth: 0 },
  skill: { flexShrink: 0 },
  nowrap: { whiteSpace: 'nowrap' },
  pager: { display: 'flex', gap: tokens.spacingHorizontalM, alignItems: 'center' },
})

/** Skill tags shown before the rest are summed up as "+N"; the tooltip lists all of them. */
const shownSkills = 3

const emptyFilters: ResumeBankFilters = {
  q: '',
  skills: '',
  minExperience: '',
  maxExperience: '',
  location: '',
  maxNoticeDays: '',
  status: '',
  contactedSince: '',
  tag: '',
  country: '',
  maxCurrentCtc: '',
  ctcCurrency: '',
}

/**
 * RB-01/04: every candidate the user may see, searchable inside CV content and by filters. HR see candidates in their
 * countries, with salary; hiring managers see the CVs, skills and past outcomes of candidates in their own country,
 * without contact details or salary (MC-03).
 */
export function ResumeBankPage() {
  const styles = useStyles()
  const { data: me } = useMe()
  const { data: countries } = useCountries()
  const isHr = hasAnyRole(me, Roles.HrAdmin, Roles.HrExecutive)
  const [form, setForm] = useState<ResumeBankFilters>(emptyFilters)
  const [filters, setFilters] = useState<ResumeBankFilters>(emptyFilters)
  const [page, setPage] = useState(1)
  const { data, isLoading, isFetching, error } = useResumeBankSearch({ ...filters, page, pageSize: 25 })

  // Typing settles before searching; any change goes back to the first page.
  useEffect(() => {
    const timer = setTimeout(() => {
      setFilters(form)
      setPage(1)
    }, 300)
    return () => clearTimeout(timer)
  }, [form])

  const set = (key: keyof ResumeBankFilters) => (value: string) => setForm((f) => ({ ...f, [key]: value }))
  const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1
  const currencies = [...new Set((countries ?? []).map((c) => c.currencyCode))]

  return (
    <section className={styles.page}>
      <div className={styles.header}>
        <Title2 as="h1">Resume Bank</Title2>
        {canImportCandidates(me) && (
          <Link to="/resume-bank/import">
            <Button icon={<ArrowUploadRegular />}>Import candidates</Button>
          </Link>
        )}
      </div>
      <Body1>
        Everyone considered for a role, past and present. Search finds words inside CVs (PDF and Word): use quotes for a phrase,
        "or" for either word and a minus sign to exclude one.
      </Body1>

      <div className={styles.filters} role="search" aria-label="Resume Bank filters">
        <SearchBox
          className={styles.search}
          aria-label="Search CVs, names and employers"
          placeholder='e.g. "data engineer" spark -intern'
          value={form.q}
          onChange={(_, d) => set('q')(d.value)}
        />
        <TextField label="Skills (all of)" value={form.skills ?? ''} onChange={set('skills')} hint="Comma-separated" />
        <TextField label="Tag" value={form.tag ?? ''} onChange={set('tag')} />
        <TextField label="Min. experience (years)" type="number" value={form.minExperience ?? ''} onChange={set('minExperience')} />
        <TextField label="Max. experience (years)" type="number" value={form.maxExperience ?? ''} onChange={set('maxExperience')} />
        <TextField label="Location" value={form.location ?? ''} onChange={set('location')} />
        <TextField label="Max. notice (days)" type="number" value={form.maxNoticeDays ?? ''} onChange={set('maxNoticeDays')} />
        <SelectField
          label="Status"
          value={form.status ?? ''}
          onChange={set('status')}
          clearLabel="Any status"
          options={resumeBankStatuses.map((s) => ({ value: s, label: statusLabels[s] }))}
        />
        <DateField label="Contacted since" value={form.contactedSince ?? ''} onChange={set('contactedSince')} />
        <SelectField
          label="Country"
          value={form.country ?? ''}
          onChange={set('country')}
          clearLabel="All countries"
          options={(countries ?? []).map((c) => ({ value: c.countryCode, label: c.name }))}
        />
        {isHr && (
          <>
            <TextField label="Max. current CTC" type="number" value={form.maxCurrentCtc ?? ''} onChange={set('maxCurrentCtc')} hint="Annual" />
            <SelectField
              label="CTC currency"
              value={form.ctcCurrency ?? ''}
              onChange={set('ctcCurrency')}
              options={currencies.map((c) => ({ value: c, label: c }))}
              error={form.maxCurrentCtc && !form.ctcCurrency ? 'Choose the currency for the salary limit.' : undefined}
            />
          </>
        )}
        <div className={styles.actions}>
          <Button appearance="subtle" onClick={() => setForm(emptyFilters)}>
            Clear filters
          </Button>
        </div>
      </div>

      {isLoading && <Spinner label="Searching…" />}
      {error && <QueryError error={error} />}
      {data && (
        <Caption1 aria-live="polite">
          {data.total === 1 ? '1 candidate' : `${data.total.toLocaleString('en-US')} candidates`}
          {isFetching ? ' · updating…' : ''}
        </Caption1>
      )}
      {data && data.items.length > 0 && (
        <div className={styles.tableWrap}>
          <Table aria-label="Resume Bank candidates" size="small">
            <TableHeader>
              <TableRow>
                <TableHeaderCell className={styles.colCandidate}>Candidate</TableHeaderCell>
                <TableHeaderCell className={styles.colStatus}>Status</TableHeaderCell>
                <TableHeaderCell className={styles.colSkills}>Skills</TableHeaderCell>
                <TableHeaderCell className={styles.colShort}>Experience</TableHeaderCell>
                <TableHeaderCell className={styles.colLocation}>Location</TableHeaderCell>
                <TableHeaderCell className={styles.colShort}>Notice</TableHeaderCell>
                {isHr && <TableHeaderCell className={styles.colMoney}>Current CTC</TableHeaderCell>}
                <TableHeaderCell className={styles.colOutcome}>Latest outcome</TableHeaderCell>
                <TableHeaderCell className={styles.colDate}>Last contacted</TableHeaderCell>
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.items.map((r) => {
                const employer = [r.currentEmployer, r.countryCode].filter(Boolean).join(' · ')
                return (
                  <TableRow key={r.personId}>
                    <TableCell>
                      <div className={styles.truncate}>
                        <span className={styles.candidate}>
                          <Link to={`/resume-bank/${r.personId}`} className={styles.truncate}>
                            {r.name}
                          </Link>
                          {r.hasCv && <DocumentRegular aria-label="Has a CV" title="Has a CV" />}
                        </span>
                        <Caption1 className={styles.truncate} title={employer}>
                          {employer}
                        </Caption1>
                      </div>
                    </TableCell>
                    <TableCell>{r.status ? <StatusBadge status={r.status as ResumeBankStatus} /> : '—'}</TableCell>
                    <TableCell>
                      {r.skills.length > 0 ? (
                        <Tooltip content={r.skills.join(', ')} relationship="description">
                          <span className={styles.skills} tabIndex={0}>
                            {r.skills.slice(0, shownSkills).map((s) => (
                              <Badge key={s} appearance="outline" color="informative" className={styles.skill}>
                                {s}
                              </Badge>
                            ))}
                            {r.skills.length > shownSkills && (
                              <Badge appearance="tint" color="informative" className={styles.skill}>
                                +{r.skills.length - shownSkills}
                              </Badge>
                            )}
                          </span>
                        </Tooltip>
                      ) : (
                        '—'
                      )}
                    </TableCell>
                    <TableCell className={styles.nowrap}>{r.experienceYears !== null ? `${r.experienceYears} yrs` : '—'}</TableCell>
                    <TableCell>
                      <span className={styles.truncate} title={r.location ?? undefined}>
                        {r.location ?? '—'}
                      </span>
                    </TableCell>
                    <TableCell className={styles.nowrap}>{r.noticePeriodDays !== null ? `${r.noticePeriodDays} days` : '—'}</TableCell>
                    {isHr && (
                      <TableCell className={styles.nowrap}>{r.currentCtc ? formatMoney(r.currentCtc.amount, r.currentCtc.currency) : '—'}</TableCell>
                    )}
                    <TableCell>
                      {r.latestOutcome ? (
                        <Tooltip content={r.latestOutcome} relationship="description">
                          <span className={styles.truncate} tabIndex={0}>
                            {r.latestOutcome}
                          </span>
                        </Tooltip>
                      ) : (
                        '—'
                      )}
                    </TableCell>
                    <TableCell className={styles.nowrap}>{formatDay(r.lastContactedAtUtc)}</TableCell>
                  </TableRow>
                )
              })}
            </TableBody>
          </Table>
        </div>
      )}
      {data && data.items.length === 0 && <Body1>No candidates match.</Body1>}
      {data && pages > 1 && (
        <nav className={styles.pager} aria-label="Pages">
          <Button disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
            Previous
          </Button>
          <Caption1>
            Page {page} of {pages}
          </Caption1>
          <Button disabled={page >= pages} onClick={() => setPage((p) => p + 1)}>
            Next
          </Button>
        </nav>
      )}
    </section>
  )
}
