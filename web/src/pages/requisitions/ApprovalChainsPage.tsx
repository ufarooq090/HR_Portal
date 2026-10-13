import { Body1, Button, Card, Spinner, Subtitle1, Title2, makeStyles, tokens } from '@fluentui/react-components'
import { AddRegular, ArrowDownRegular, ArrowUpRegular, DeleteRegular } from '@fluentui/react-icons'
import { useState } from 'react'
import { ApiError } from '../../api/client'
import { useEmployees } from '../../api/employees'
import { useMe } from '../../api/queries'
import { useApprovalChains, useSaveApprovalChain } from '../../api/requisitions'
import type { ApprovalChain } from '../../api/types'
import { QueryError } from '../../app/QueryError'
import { canConfigureCountry, canManageOrganisation } from '../../auth/roles'
import { SelectField, TextField } from '../employees/fields'

const useStyles = makeStyles({
  page: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL, maxWidth: '900px' },
  card: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM, padding: tokens.spacingHorizontalL },
  step: { display: 'grid', gridTemplateColumns: 'minmax(140px, 1fr) minmax(200px, 2fr) auto', gap: tokens.spacingHorizontalM, alignItems: 'end' },
  actions: { display: 'flex', gap: tokens.spacingHorizontalXS },
})

/** REQ-02: who approves requisitions in each country, in order. HR's default is a single CEO approval. */
export function ApprovalChainsPage() {
  const styles = useStyles()
  const { data: chains, isLoading, error } = useApprovalChains()

  return (
    <section className={styles.page}>
      <Title2 as="h1">Approval chains</Title2>
      <Body1>
        Requisitions are approved by these people, in order. The default is a single CEO approval. Changes apply to
        requisitions submitted afterwards.
      </Body1>
      {isLoading && <Spinner label="Loading…" />}
      {error && <QueryError error={error} />}
      {chains?.map((chain) => <ChainEditor key={chain.countryCode} chain={chain} />)}
    </section>
  )
}

function ChainEditor({ chain }: { chain: ApprovalChain }) {
  const styles = useStyles()
  const { data: me } = useMe()
  const { data: people } = useEmployees({ status: ['Probation', 'Confirmed', 'OnNotice'] })
  const save = useSaveApprovalChain()
  const [steps, setSteps] = useState(chain.steps.map((s) => ({ label: s.label, approverEmployeeId: s.approverEmployeeId })))
  const [saved, setSaved] = useState(false)
  const editable = canManageOrganisation(me) && canConfigureCountry(me, chain.countryCode)
  const apiError = save.error instanceof ApiError ? save.error : undefined
  // The current approvers may be outside the list this user can see, so keep their names as options.
  const options = [
    ...chain.steps.map((s) => ({ value: s.approverEmployeeId, label: s.approverName })),
    ...(people ?? []).filter((p) => !chain.steps.some((s) => s.approverEmployeeId === p.id)).map((p) => ({ value: p.id, label: `${p.fullName} (${p.employeeNumber})` })),
  ]
  const update = (i: number, change: Partial<(typeof steps)[number]>) => {
    setSaved(false)
    setSteps((current) => current.map((s, j) => (j === i ? { ...s, ...change } : s)))
  }
  const move = (i: number, by: number) =>
    setSteps((current) => {
      const next = [...current]
      ;[next[i], next[i + by]] = [next[i + by]!, next[i]!]
      return next
    })

  return (
    <Card className={styles.card} aria-label={`${chain.countryName} approval chain`}>
      <Subtitle1 as="h2">{chain.countryName}</Subtitle1>
      {chain.steps.length === 0 && <Body1>No chain yet: requisitions in {chain.countryName} cannot be submitted until one is set up.</Body1>}
      {chain.steps.some((s) => !s.approverActive) && <QueryError error={new ApiError(400, { title: 'An approver has left. Choose someone else.' })} />}
      {steps.map((s, i) => (
        <div key={i} className={styles.step}>
          <TextField label={`Step ${i + 1}`} value={s.label} onChange={(v) => update(i, { label: v })} hint={i === 0 ? 'e.g. CEO' : undefined} />
          <SelectField label="Approver" value={s.approverEmployeeId} onChange={(v) => update(i, { approverEmployeeId: v })} options={options} />
          {editable && (
            <div className={styles.actions}>
              <Button appearance="subtle" icon={<ArrowUpRegular />} aria-label={`Move step ${i + 1} up`} disabled={i === 0} onClick={() => move(i, -1)} />
              <Button appearance="subtle" icon={<ArrowDownRegular />} aria-label={`Move step ${i + 1} down`} disabled={i === steps.length - 1} onClick={() => move(i, 1)} />
              <Button appearance="subtle" icon={<DeleteRegular />} aria-label={`Remove step ${i + 1}`} onClick={() => setSteps(steps.filter((_, j) => j !== i))} />
            </div>
          )}
        </div>
      ))}
      {editable && (
        <div className={styles.actions}>
          <Button icon={<AddRegular />} disabled={steps.length >= 5} onClick={() => setSteps([...steps, { label: '', approverEmployeeId: '' }])}>
            Add step
          </Button>
          <Button
            appearance="primary"
            disabled={save.isPending}
            onClick={() => save.mutate({ countryCode: chain.countryCode, steps }, { onSuccess: () => setSaved(true) })}
          >
            Save {chain.countryName} chain
          </Button>
        </div>
      )}
      {saved && <Body1 role="status">Saved.</Body1>}
      {apiError && <QueryError error={apiError.problem?.errors ? new ApiError(400, { title: apiError.fieldError('steps') ?? apiError.message }) : apiError} />}
    </Card>
  )
}
