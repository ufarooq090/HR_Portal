import { Body1, Button, Card, Spinner, Subtitle1, Title2, makeStyles, tokens } from '@fluentui/react-components'
import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { ApiError } from '../../api/client'
import { useEmployee, useEmployees } from '../../api/employees'
import { useCountries, useMe, useOrganisationItems } from '../../api/queries'
import { useCreateRequisition, useRequisition, useUpdateRequisition } from '../../api/requisitions'
import type { Department, OfficeLocation, RequisitionDetail, RequisitionReason, RequisitionRequest } from '../../api/types'
import { QueryError } from '../../app/QueryError'
import { canCreateEmployees } from '../../auth/roles'
import { DateField, SelectField, TextField } from '../employees/fields'
import { todayIso } from '../employees/format'
import { fieldGrid } from '../../app/layout'

const useStyles = makeStyles({
  page: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL, maxWidth: '1000px' },
  card: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM, padding: tokens.spacingHorizontalL },
  grid: fieldGrid,
  actions: { display: 'flex', gap: tokens.spacingHorizontalS },
})

/** /requisitions/new and /requisitions/:id/edit (drafts only). */
export function RequisitionFormPage() {
  const { id } = useParams()
  const existing = useRequisition(id ?? '')
  if (id && existing.isLoading) return <Spinner label="Loading…" />
  if (id && existing.error) return <QueryError error={existing.error} />
  return <RequisitionForm existing={id ? existing.data : undefined} />
}

interface FormState {
  title: string
  departmentId: string
  officeLocationId: string
  openings: string
  budgetMin: string
  budgetMax: string
  reason: RequisitionReason
  replacedEmployeeId: string
  targetDate: string
  jobDescription: string
  hiringManagerEmployeeId: string
}

/** REQ-01: role, department, openings, budget range in local currency, reason, target date, job description. */
function RequisitionForm({ existing }: { existing: RequisitionDetail | undefined }) {
  const styles = useStyles()
  const navigate = useNavigate()
  const { data: me } = useMe()
  const { data: self } = useEmployee(me?.employeeId ?? '')
  const { data: departments } = useOrganisationItems<Department>('departments')
  const { data: offices } = useOrganisationItems<OfficeLocation>('locations')
  const { data: countries } = useCountries()
  const create = useCreateRequisition()
  const update = useUpdateRequisition()
  const mutation = existing ? update : create
  const [form, setForm] = useState<FormState>(() =>
    existing
      ? {
          ...existing,
          openings: String(existing.openings),
          budgetMin: String(existing.budgetMin),
          budgetMax: String(existing.budgetMax),
          replacedEmployeeId: existing.replacedEmployeeId ?? '',
          hiringManagerEmployeeId: existing.hiringManagerEmployeeId,
        }
      : {
          title: '',
          departmentId: '',
          officeLocationId: '',
          openings: '1',
          budgetMin: '',
          budgetMax: '',
          reason: 'NewPosition',
          replacedEmployeeId: '',
          targetDate: todayIso(new Date(Date.now() + 60 * 86_400_000)),
          jobDescription: '',
          hiringManagerEmployeeId: '',
        },
  )
  // Managers can only raise for their own department, so start there.
  const departmentId = form.departmentId || (existing ? '' : (self?.departmentId ?? ''))
  const { data: people } = useEmployees({ departmentId, status: ['Probation', 'Confirmed', 'OnNotice'] })
  const { data: everyone } = useEmployees({ status: ['Probation', 'Confirmed', 'OnNotice'] })
  const isHr = canCreateEmployees(me)
  const office = offices?.find((o) => o.id === form.officeLocationId)
  const currency = countries?.find((c) => c.countryCode === office?.countryCode)?.currencyCode
  const apiError = mutation.error instanceof ApiError ? mutation.error : undefined
  const error = (field: string) => apiError?.fieldError(field)
  const set = <K extends keyof FormState>(key: K, value: FormState[K]) => setForm((f) => ({ ...f, [key]: value }))
  const amount = (text: string) => Number(text.replace(/,/g, '')) || 0

  const submit = () => {
    const body: RequisitionRequest = {
      title: form.title,
      departmentId,
      officeLocationId: form.officeLocationId,
      openings: Number.parseInt(form.openings, 10) || 0,
      budgetMin: amount(form.budgetMin),
      budgetMax: amount(form.budgetMax),
      reason: form.reason,
      replacedEmployeeId: form.reason === 'Replacement' ? form.replacedEmployeeId || null : null,
      targetDate: form.targetDate,
      jobDescription: form.jobDescription,
      hiringManagerEmployeeId: form.hiringManagerEmployeeId || null,
    }
    const done = (r: RequisitionDetail) => navigate(`/requisitions/${r.id}`)
    if (existing) update.mutate({ id: existing.id, body }, { onSuccess: done })
    else create.mutate(body, { onSuccess: done })
  }

  return (
    <section className={styles.page}>
      <Title2 as="h1">{existing ? `Edit ${existing.number}` : 'Raise requisition'}</Title2>
      <Body1>
        <Link to={existing ? `/requisitions/${existing.id}` : '/requisitions'}>Back</Link>
      </Body1>
      <form
        className={styles.page}
        onSubmit={(e) => {
          e.preventDefault()
          submit()
        }}
      >
        <Card className={styles.card}>
          <Subtitle1 as="h2">Position</Subtitle1>
          <div className={styles.grid}>
            <TextField label="Role" required value={form.title} onChange={(v) => set('title', v)} error={error('title')} hint="e.g. Senior Backend Engineer" />
            <SelectField
              label="Department"
              required
              value={departmentId}
              onChange={(v) => set('departmentId', v)}
              options={(departments ?? []).filter((d) => d.isActive).map((d) => ({ value: d.id, label: d.name }))}
              error={error('departmentId')}
              hint="Managers raise requisitions for their own department."
            />
            <SelectField
              label="Office"
              required
              value={form.officeLocationId}
              onChange={(v) => set('officeLocationId', v)}
              options={(offices ?? []).filter((o) => o.isActive).map((o) => ({ value: o.id, label: `${o.name} (${o.countryCode})` }))}
              error={error('officeLocationId')}
              hint="Sets the country and the budget currency."
            />
            <TextField label="Openings" required type="number" value={form.openings} onChange={(v) => set('openings', v)} error={error('openings')} />
            <TextField
              label={`Budget from${currency ? ` (${currency})` : ''}`}
              required
              value={form.budgetMin}
              onChange={(v) => set('budgetMin', v)}
              hint="Annual cost to company, in the office's currency."
              error={error('budgetMin') ?? error('currencyCode')}
            />
            <TextField label={`Budget to${currency ? ` (${currency})` : ''}`} required value={form.budgetMax} onChange={(v) => set('budgetMax', v)} error={error('budgetMax')} />
            <DateField label="Target date" required value={form.targetDate} onChange={(v) => set('targetDate', v)} error={error('targetDate')} />
            <SelectField
              label="Reason"
              required
              value={form.reason}
              onChange={(v) => set('reason', v as RequisitionReason)}
              options={[
                { value: 'NewPosition', label: 'New position' },
                { value: 'Replacement', label: 'Replacement' },
              ]}
            />
            {isHr && (
              <SelectField
                label="Hiring manager"
                value={form.hiringManagerEmployeeId}
                onChange={(v) => set('hiringManagerEmployeeId', v)}
                clearLabel="The requester"
                options={(everyone ?? []).map((p) => ({ value: p.id, label: `${p.fullName} (${p.employeeNumber})` }))}
                error={error('hiringManagerEmployeeId')}
                hint="Sees this requisition's candidates."
              />
            )}
            {form.reason === 'Replacement' && (
              <SelectField
                label="Replacing"
                required
                value={form.replacedEmployeeId}
                onChange={(v) => set('replacedEmployeeId', v)}
                options={(people ?? []).map((p) => ({ value: p.id, label: `${p.fullName} (${p.employeeNumber})` }))}
                error={error('replacedEmployeeId')}
                hint="Someone in this department."
              />
            )}
          </div>
        </Card>

        <Card className={styles.card}>
          <Subtitle1 as="h2">Job description</Subtitle1>
          <TextField label="Job description" required multiline value={form.jobDescription} onChange={(v) => set('jobDescription', v)} error={error('jobDescription')} />
        </Card>

        {mutation.error && !apiError?.problem?.errors && <QueryError error={mutation.error} />}
        <div className={styles.actions}>
          <Button appearance="primary" type="submit" disabled={mutation.isPending}>
            {mutation.isPending ? 'Saving…' : 'Save draft'}
          </Button>
          <Button onClick={() => navigate(-1)}>Cancel</Button>
        </div>
      </form>
    </section>
  )
}
