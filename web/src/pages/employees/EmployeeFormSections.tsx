import { Button, Caption1, makeStyles, Subtitle2, tokens } from '@fluentui/react-components'
import { AddRegular, DeleteRegular } from '@fluentui/react-icons'
import { useEmployees } from '../../api/employees'
import { useCountries, useOrganisationItems } from '../../api/queries'
import type { Department, Grade, LegalEntity, OfficeLocation, PersonalDetails } from '../../api/types'
import { SelectField, TextField } from './fields'
import type { FieldError } from './personal'

const useStyles = makeStyles({
  section: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  grid: { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(240px, 1fr))', gap: tokens.spacingHorizontalL },
  contact: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fill, minmax(180px, 1fr)) auto',
    gap: tokens.spacingHorizontalM,
    alignItems: 'end',
  },
})

/** Personal details (EMP-01). Shown and edited only by HR and, for viewing, the employee. */
export function PersonalSection({
  value,
  onChange,
  error,
}: {
  value: PersonalDetails
  onChange: (value: PersonalDetails) => void
  error: FieldError
}) {
  const styles = useStyles()
  const { data: countries } = useCountries()
  const text = (field: keyof Omit<PersonalDetails, 'emergencyContacts'>) => ({
    value: value[field] ?? '',
    onChange: (v: string) => onChange({ ...value, [field]: v === '' ? null : v }),
    error: error(field),
  })
  const contacts = value.emergencyContacts
  const setContact = (index: number, field: 'name' | 'relationship' | 'phone', v: string) =>
    onChange({ ...value, emergencyContacts: contacts.map((c, i) => (i === index ? { ...c, [field]: v } : c)) })

  return (
    <div className={styles.section}>
      <div className={styles.grid}>
        <TextField label="Personal email" type="email" {...text('personalEmail')} />
        <TextField label="Phone" type="tel" {...text('phone')} />
        <TextField label="Date of birth" type="date" {...text('dateOfBirth')} />
        <TextField label="National ID (CNIC / SSN)" hint="Stored encrypted; visible to HR and the employee only." {...text('nationalId')} />
        <TextField label="Address line 1" {...text('addressLine1')} />
        <TextField label="Address line 2" {...text('addressLine2')} />
        <TextField label="City" {...text('city')} />
        <TextField label="Province / state" {...text('region')} />
        <TextField label="Postal code" {...text('postalCode')} />
        <SelectField
          label="Address country"
          value={value.addressCountryCode ?? ''}
          onChange={(v) => onChange({ ...value, addressCountryCode: v || null })}
          clearLabel="Not set"
          options={(countries ?? []).map((c) => ({ value: c.countryCode, label: c.name }))}
          error={error('addressCountryCode')}
        />
      </div>

      <Subtitle2 as="h3">Emergency contacts</Subtitle2>
      {error('emergencyContacts') && <Caption1 role="alert">{error('emergencyContacts')}</Caption1>}
      {contacts.map((c, i) => (
        <div key={i} className={styles.contact}>
          <TextField label={`Contact ${i + 1} name`} value={c.name} onChange={(v) => setContact(i, 'name', v)} required />
          <TextField label="Relationship" value={c.relationship} onChange={(v) => setContact(i, 'relationship', v)} required />
          <TextField label="Phone" type="tel" value={c.phone} onChange={(v) => setContact(i, 'phone', v)} required />
          <Button
            appearance="subtle"
            icon={<DeleteRegular />}
            aria-label={`Remove contact ${i + 1}`}
            onClick={() => onChange({ ...value, emergencyContacts: contacts.filter((_, j) => j !== i) })}
          />
        </div>
      ))}
      {contacts.length < 3 && (
        <div>
          <Button
            icon={<AddRegular />}
            onClick={() => onChange({ ...value, emergencyContacts: [...contacts, { name: '', relationship: '', phone: '' }] })}
          >
            Add emergency contact
          </Button>
        </div>
      )}
    </div>
  )
}

export interface Placement {
  designation: string
  legalEntityId: string
  officeLocationId: string
  departmentId: string
  gradeId: string
}

/**
 * Designation, legal entity, office, department and grade (EMP-02). The legal entity sets the country, and the
 * office and grade lists only offer that country's items (MC-01). Only active items are offered.
 */
export function PlacementSection({ value, onChange, error }: { value: Placement; onChange: (value: Placement) => void; error: FieldError }) {
  const styles = useStyles()
  const { data: entities } = useOrganisationItems<LegalEntity>('legal-entities')
  const { data: offices } = useOrganisationItems<OfficeLocation>('locations')
  const { data: departments } = useOrganisationItems<Department>('departments')
  const { data: grades } = useOrganisationItems<Grade>('grades')
  const country = entities?.find((e) => e.id === value.legalEntityId)?.countryCode

  return (
    <div className={styles.grid}>
      <TextField
        label="Designation"
        required
        value={value.designation}
        onChange={(designation) => onChange({ ...value, designation })}
        error={error('designation')}
      />
      <SelectField
        label="Legal entity"
        required
        value={value.legalEntityId}
        onChange={(legalEntityId) => {
          const next = entities?.find((e) => e.id === legalEntityId)?.countryCode
          // Office and grade belong to one country; clear them when the country changes.
          onChange(next === country ? { ...value, legalEntityId } : { ...value, legalEntityId, officeLocationId: '', gradeId: '' })
        }}
        options={(entities ?? []).filter((e) => e.isActive).map((e) => ({ value: e.id, label: `${e.name} (${e.countryCode})` }))}
        error={error('legalEntityId')}
      />
      <SelectField
        label="Office"
        required
        value={value.officeLocationId}
        onChange={(officeLocationId) => onChange({ ...value, officeLocationId })}
        placeholder={country ? 'Select…' : 'Choose a legal entity first'}
        options={(offices ?? []).filter((o) => o.isActive && o.countryCode === country).map((o) => ({ value: o.id, label: o.name }))}
        error={error('officeLocationId')}
      />
      <SelectField
        label="Department"
        required
        value={value.departmentId}
        onChange={(departmentId) => onChange({ ...value, departmentId })}
        options={(departments ?? []).filter((d) => d.isActive).map((d) => ({ value: d.id, label: d.name }))}
        error={error('departmentId')}
      />
      <SelectField
        label="Grade"
        value={value.gradeId}
        onChange={(gradeId) => onChange({ ...value, gradeId })}
        clearLabel="No grade"
        options={(grades ?? []).filter((g) => g.isActive && g.countryCode === country).map((g) => ({ value: g.id, label: `${g.code} – ${g.name}` }))}
        error={error('gradeId')}
      />
    </div>
  )
}

/** Active employees the user can see, as manager options; never the employee themselves. */
export function ManagerField({
  value,
  onChange,
  excludeId,
  error,
}: {
  value: string
  onChange: (value: string) => void
  excludeId?: string
  error?: string
}) {
  const { data: people } = useEmployees({ status: ['Probation', 'Confirmed', 'OnNotice'] })
  return (
    <SelectField
      label="Reporting manager"
      value={value}
      onChange={onChange}
      clearLabel="No manager"
      options={(people ?? [])
        .filter((p) => p.id !== excludeId)
        .map((p) => ({ value: p.id, label: `${p.fullName} (${p.employeeNumber})` }))}
      error={error}
    />
  )
}
