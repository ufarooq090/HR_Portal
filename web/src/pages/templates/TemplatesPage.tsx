import {
  Badge,
  Body1,
  Button,
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
import { AddRegular } from '@fluentui/react-icons'
import { useState } from 'react'
import { Link } from 'react-router'
import { useMe } from '../../api/queries'
import { recordTypeLabels, templateCategories, templateRecordTypes, useTemplates } from '../../api/templates'
import { QueryError } from '../../app/QueryError'
import { canManageTemplates } from '../../auth/roles'
import { SelectField } from '../employees/fields'

const useStyles = makeStyles({
  page: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL, maxWidth: '1200px' },
  header: { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: tokens.spacingHorizontalM, flexWrap: 'wrap' },
  filters: { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))', gap: tokens.spacingHorizontalM },
  tableWrap: { overflowX: 'auto' },
})

/** ET-06: templates by category, with their status, country and version. */
export function TemplatesPage() {
  const styles = useStyles()
  const { data: me } = useMe()
  const { data, isLoading, error } = useTemplates()
  const [category, setCategory] = useState('')
  const [recordType, setRecordType] = useState('')
  const rows = (data ?? []).filter((t) => (!category || t.category === category) && (!recordType || t.recordType === recordType))

  return (
    <section className={styles.page}>
      <div className={styles.header}>
        <Title2 as="h1">Email templates</Title2>
        {canManageTemplates(me) && (
          <Link to="/templates/new">
            <Button appearance="primary" icon={<AddRegular />}>
              New template
            </Button>
          </Link>
        )}
      </div>
      <Body1>Emails are sent from a candidate, employee or requisition, and fields such as {'{{first_name}}'} fill in from that record.</Body1>
      <div className={styles.filters}>
        <SelectField label="Category" value={category} onChange={setCategory} clearLabel="All categories" options={templateCategories.map((c) => ({ value: c, label: c }))} />
        <SelectField
          label="Sent to"
          value={recordType}
          onChange={setRecordType}
          clearLabel="Everyone"
          options={templateRecordTypes.map((t) => ({ value: t, label: recordTypeLabels[t] }))}
        />
      </div>
      {isLoading && <Spinner label="Loading templates…" />}
      {error && <QueryError error={error} />}
      {data && (
        <div className={styles.tableWrap}>
          <Table aria-label="Email templates">
            <TableHeader>
              <TableRow>
                <TableHeaderCell>Name</TableHeaderCell>
                <TableHeaderCell>Category</TableHeaderCell>
                <TableHeaderCell>Sent to</TableHeaderCell>
                <TableHeaderCell>Country</TableHeaderCell>
                <TableHeaderCell>Status</TableHeaderCell>
                <TableHeaderCell>Version</TableHeaderCell>
                <TableHeaderCell>Updated</TableHeaderCell>
              </TableRow>
            </TableHeader>
            <TableBody>
              {rows.map((t) => (
                <TableRow key={t.id}>
                  <TableCell>
                    <Link to={`/templates/${t.id}`}>{t.name}</Link>{' '}
                    {t.isRestricted && (
                      <Badge appearance="outline" color="danger">
                        HR Admin only
                      </Badge>
                    )}
                  </TableCell>
                  <TableCell>{t.category}</TableCell>
                  <TableCell>{recordTypeLabels[t.recordType]}</TableCell>
                  <TableCell>{t.countryCode ?? 'Both'}</TableCell>
                  <TableCell>
                    <Badge appearance="tint" color={t.isActive ? 'success' : 'subtle'}>
                      {t.isActive ? 'Active' : 'Inactive'}
                    </Badge>
                  </TableCell>
                  <TableCell>{t.version}</TableCell>
                  <TableCell>{new Date(t.updatedAtUtc).toLocaleDateString()}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          {rows.length === 0 && <Body1>No templates match.</Body1>}
        </div>
      )}
    </section>
  )
}
