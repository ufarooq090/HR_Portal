import { Body1, Button, Card, makeStyles, Subtitle1, Title2, tokens } from '@fluentui/react-components'
import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { ApiError } from '../../api/client'
import { useCreateEmployee } from '../../api/employees'
import { type EmploymentType, employmentTypes, type PersonalDetails } from '../../api/types'
import { QueryError } from '../../app/QueryError'
import { ManagerField, PersonalSection, type Placement, PlacementSection } from './EmployeeFormSections'
import { SelectField, TextField } from './fields'
import { label, todayIso } from './format'
import { cleanPersonal, emptyPersonal } from './personal'

const useStyles = makeStyles({
  page: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL, maxWidth: '1100px' },
  card: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM, padding: tokens.spacingHorizontalL },
  grid: { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(240px, 1fr))', gap: tokens.spacingHorizontalL },
  actions: { display: 'flex', gap: tokens.spacingHorizontalS },
})

/** EMP-01/02: add one employee. Status starts at probation (or confirmed when the country/grade has none). */
export function NewEmployeePage() {
  const styles = useStyles()
  const navigate = useNavigate()
  const create = useCreateEmployee()
  const [identity, setIdentity] = useState({ givenName: '', familyName: '', employeeNumber: '', workEmail: '' })
  const [employmentType, setEmploymentType] = useState<EmploymentType>('Permanent')
  const [placement, setPlacement] = useState<Placement>({ designation: '', legalEntityId: '', officeLocationId: '', departmentId: '', gradeId: '' })
  const [managerId, setManagerId] = useState('')
  const [joiningDate, setJoiningDate] = useState(todayIso())
  const [personal, setPersonal] = useState<PersonalDetails>(emptyPersonal)
  const apiError = create.error instanceof ApiError ? create.error : undefined
  const error = (field: string) => apiError?.fieldError(field)
  const id = (field: keyof typeof identity) => ({
    value: identity[field],
    onChange: (v: string) => setIdentity({ ...identity, [field]: v }),
    error: error(field),
    required: true,
  })

  const submit = () =>
    create.mutate(
      {
        ...identity,
        employmentType,
        ...placement,
        gradeId: placement.gradeId || null,
        managerId: managerId || null,
        joiningDate,
        personal: cleanPersonal(personal),
      },
      { onSuccess: (employee) => navigate(`/people/${employee.id}`) },
    )

  return (
    <section className={styles.page}>
      <Title2 as="h1">Add employee</Title2>
      <Body1>
        <Link to="/people">Back to People</Link>
      </Body1>
      <form
        onSubmit={(e) => {
          e.preventDefault()
          submit()
        }}
        className={styles.page}
      >
        <Card className={styles.card}>
          <Subtitle1 as="h2">Identity</Subtitle1>
          <div className={styles.grid}>
            <TextField label="Given name" {...id('givenName')} />
            <TextField label="Family name" {...id('familyName')} />
            <TextField label="Employee ID" hint="e.g. PK-0042" {...id('employeeNumber')} />
            <TextField label="Work email" type="email" {...id('workEmail')} />
          </div>
        </Card>

        <Card className={styles.card}>
          <Subtitle1 as="h2">Job</Subtitle1>
          <PlacementSection value={placement} onChange={setPlacement} error={error} />
          <div className={styles.grid}>
            <SelectField
              label="Employment type"
              required
              value={employmentType}
              onChange={(v) => setEmploymentType(v as EmploymentType)}
              options={employmentTypes.map((t) => ({ value: t, label: label(t) }))}
            />
            <TextField label="Joining date" type="date" required value={joiningDate} onChange={setJoiningDate} error={error('joiningDate')} />
            <ManagerField value={managerId} onChange={setManagerId} error={error('managerId')} />
          </div>
        </Card>

        <Card className={styles.card}>
          <Subtitle1 as="h2">Personal details</Subtitle1>
          <PersonalSection value={personal} onChange={setPersonal} error={error} />
        </Card>

        {create.error && !apiError?.problem?.errors && <QueryError error={create.error} />}
        {apiError?.problem?.errors && <QueryError error={new ApiError(400, { title: 'Check the highlighted fields.' })} />}
        <div className={styles.actions}>
          <Button appearance="primary" type="submit" disabled={create.isPending}>
            {create.isPending ? 'Saving…' : 'Add employee'}
          </Button>
          <Button onClick={() => navigate('/people')}>Cancel</Button>
        </div>
      </form>
    </section>
  )
}
