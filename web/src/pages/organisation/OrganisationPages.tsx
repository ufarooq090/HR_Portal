import {
  useCountries,
  useCreateCountry,
  useDeleteOrganisationItem,
  useMe,
  useOrganisationItems,
  useSaveOrganisationItem,
  useUpdateCountry,
} from '../../api/queries'
import type {
  CountrySettings,
  CreateCountryRequest,
  Department,
  Grade,
  LegalEntity,
  OfficeLocation,
  UpdateCountrySettingsRequest,
} from '../../api/types'
import { canConfigureCountry, canManageCountrySettings, canManageOrganisation } from '../../auth/roles'
import type { FieldDef } from './forms'
import { ActiveBadge, ReferenceDataPage } from './ReferenceDataPage'

const country: FieldDef = { name: 'countryCode', label: 'Country', kind: 'country', required: true, createOnly: true }
const active: FieldDef = { name: 'isActive', label: 'Active', kind: 'active' }

/** Pages are read-only for everyone except HR Admins, who may change only the countries they can access (MC-03). */
function useOrganisationPermissions() {
  const { data: me } = useMe()
  return {
    canCreate: canManageOrganisation(me),
    canEditCountry: (code: string) => canManageOrganisation(me) && canConfigureCountry(me, code),
  }
}

export function CountriesPage() {
  const { data: me } = useMe()
  const { data, isLoading, error } = useCountries()
  const create = useCreateCountry()
  const update = useUpdateCountry()

  return (
    <ReferenceDataPage<CountrySettings>
      title="Countries"
      description="Country defaults cascade to offices and grades, which can override them. Leave, holidays and templates will be configured per country too."
      noun="country"
      items={data}
      isLoading={isLoading}
      error={error}
      getKey={(c) => c.countryCode}
      getName={(c) => c.name}
      columns={[
        { header: 'Country', render: (c) => `${c.name} (${c.countryCode})` },
        { header: 'Currency', render: (c) => c.currencyCode },
        { header: 'Time zone', render: (c) => c.defaultTimeZone },
        { header: 'Probation', render: (c) => `${c.probationMonths} months` },
        { header: 'Notice period', render: (c) => `${c.noticePeriodDays} days` },
      ]}
      fields={[
        { name: 'countryCode', label: 'ISO country code', kind: 'code', required: true, createOnly: true, hint: 'Two letters, e.g. PK' },
        { name: 'name', label: 'Name', kind: 'text', required: true },
        { name: 'currencyCode', label: 'Currency', kind: 'code', required: true, createOnly: true, hint: 'ISO code, e.g. PKR. Cannot be changed later.' },
        { name: 'defaultTimeZone', label: 'Default time zone', kind: 'timeZone', required: true },
        { name: 'probationMonths', label: 'Probation (months)', kind: 'number', required: true },
        { name: 'noticePeriodDays', label: 'Notice period (days)', kind: 'number', required: true },
      ]}
      // Adding a country widens every all-country HR user's scope, so the API requires an unrestricted admin.
      canCreate={canManageCountrySettings(me) && (me?.roles.includes('SystemAdmin') || me?.hrCountries.all === true)}
      canEdit={(c) => canManageCountrySettings(me) && canConfigureCountry(me, c.countryCode)}
      onSave={(item, body) =>
        item
          ? update.mutateAsync({ countryCode: item.countryCode, ...(body as unknown as UpdateCountrySettingsRequest) })
          : create.mutateAsync(body as unknown as CreateCountryRequest)
      }
    />
  )
}

export function LegalEntitiesPage() {
  const { data, isLoading, error } = useOrganisationItems<LegalEntity>('legal-entities')
  const save = useSaveOrganisationItem<LegalEntity>('legal-entities')
  const remove = useDeleteOrganisationItem('legal-entities')
  const { canCreate, canEditCountry } = useOrganisationPermissions()

  return (
    <ReferenceDataPage<LegalEntity>
      title="Legal entities"
      description="The companies employees are contracted with. Used for letterhead and payroll exports."
      noun="legal entity"
      items={data}
      isLoading={isLoading}
      error={error}
      getKey={(e) => e.id}
      getName={(e) => e.name}
      columns={[
        { header: 'Name', render: (e) => e.name },
        { header: 'Country', render: (e) => e.countryCode },
        { header: 'Status', render: (e) => <ActiveBadge active={e.isActive} /> },
      ]}
      fields={[country, { name: 'name', label: 'Name', kind: 'text', required: true }, active]}
      canCreate={canCreate}
      canEdit={(e) => canEditCountry(e.countryCode)}
      onSave={(item, body) => save.mutateAsync({ id: item?.id, body })}
      onDelete={(item) => remove.mutateAsync(item.id)}
    />
  )
}

export function OfficeLocationsPage() {
  const { data, isLoading, error } = useOrganisationItems<OfficeLocation>('locations')
  const save = useSaveOrganisationItem<OfficeLocation>('locations')
  const remove = useDeleteOrganisationItem('locations')
  const { canCreate, canEditCountry } = useOrganisationPermissions()

  return (
    <ReferenceDataPage<OfficeLocation>
      title="Office locations"
      description="Offices inherit their country's settings. Set a time zone only if it differs from the country default."
      noun="office"
      items={data}
      isLoading={isLoading}
      error={error}
      getKey={(l) => l.id}
      getName={(l) => l.name}
      columns={[
        { header: 'Name', render: (l) => l.name },
        { header: 'Country', render: (l) => l.countryCode },
        { header: 'City', render: (l) => l.city ?? '—' },
        { header: 'Time zone', render: (l) => (l.timeZone ? l.effectiveTimeZone : `${l.effectiveTimeZone} (country default)`) },
        { header: 'Status', render: (l) => <ActiveBadge active={l.isActive} /> },
      ]}
      fields={[
        country,
        { name: 'name', label: 'Name', kind: 'text', required: true },
        { name: 'city', label: 'City', kind: 'text' },
        { name: 'timeZone', label: 'Time zone override', kind: 'timeZone', hint: 'Leave empty to use the country default.' },
        active,
      ]}
      canCreate={canCreate}
      canEdit={(l) => canEditCountry(l.countryCode)}
      onSave={(item, body) => save.mutateAsync({ id: item?.id, body })}
      onDelete={(item) => remove.mutateAsync(item.id)}
    />
  )
}

export function DepartmentsPage() {
  const { data, isLoading, error } = useOrganisationItems<Department>('departments')
  const save = useSaveOrganisationItem<Department>('departments')
  const remove = useDeleteOrganisationItem('departments')
  const { canCreate } = useOrganisationPermissions()

  return (
    <ReferenceDataPage<Department>
      title="Departments"
      description="Departments are shared across countries."
      noun="department"
      items={data}
      isLoading={isLoading}
      error={error}
      getKey={(d) => d.id}
      getName={(d) => d.name}
      columns={[
        { header: 'Code', render: (d) => d.code },
        { header: 'Name', render: (d) => d.name },
        { header: 'Status', render: (d) => <ActiveBadge active={d.isActive} /> },
      ]}
      fields={[
        { name: 'code', label: 'Code', kind: 'code', required: true, hint: 'Short and unique, e.g. ENG' },
        { name: 'name', label: 'Name', kind: 'text', required: true },
        active,
      ]}
      canCreate={canCreate}
      canEdit={() => canCreate}
      onSave={(item, body) => save.mutateAsync({ id: item?.id, body })}
      onDelete={(item) => remove.mutateAsync(item.id)}
    />
  )
}

export function GradesPage() {
  const { data, isLoading, error } = useOrganisationItems<Grade>('grades')
  const save = useSaveOrganisationItem<Grade>('grades')
  const remove = useDeleteOrganisationItem('grades')
  const { canCreate, canEditCountry } = useOrganisationPermissions()
  const term = (value: number | null, effective: number, unit: string) =>
    value === null ? `${effective} ${unit} (country default)` : `${effective} ${unit}`

  return (
    <ReferenceDataPage<Grade>
      title="Grades"
      description="Grades per country. A grade can override the country's probation and notice period."
      noun="grade"
      items={data}
      isLoading={isLoading}
      error={error}
      getKey={(g) => g.id}
      getName={(g) => `${g.countryCode} ${g.code}`}
      columns={[
        { header: 'Country', render: (g) => g.countryCode },
        { header: 'Code', render: (g) => g.code },
        { header: 'Name', render: (g) => g.name },
        { header: 'Level', render: (g) => g.level },
        { header: 'Probation', render: (g) => term(g.probationMonths, g.effectiveProbationMonths, 'months') },
        { header: 'Notice period', render: (g) => term(g.noticePeriodDays, g.effectiveNoticePeriodDays, 'days') },
        { header: 'Status', render: (g) => <ActiveBadge active={g.isActive} /> },
      ]}
      fields={[
        country,
        { name: 'code', label: 'Code', kind: 'code', required: true, hint: 'Unique within the country, e.g. G3' },
        { name: 'name', label: 'Name', kind: 'text', required: true },
        { name: 'level', label: 'Level', kind: 'number', required: true, hint: 'Higher is more senior.' },
        { name: 'probationMonths', label: 'Probation override (months)', kind: 'optionalNumber', hint: 'Leave empty to use the country default.' },
        { name: 'noticePeriodDays', label: 'Notice period override (days)', kind: 'optionalNumber', hint: 'Leave empty to use the country default.' },
        active,
      ]}
      canCreate={canCreate}
      canEdit={(g) => canEditCountry(g.countryCode)}
      onSave={(item, body) => save.mutateAsync({ id: item?.id, body })}
      onDelete={(item) => remove.mutateAsync(item.id)}
    />
  )
}
