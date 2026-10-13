import {
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
  makeStyles,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  tokens,
} from '@fluentui/react-components'
import { useState } from 'react'
import { ApiError } from '../../api/client'
import { useCountries } from '../../api/queries'
import { checkDuplicates, useAddApplication, useMoveStage, useRejectionReasons } from '../../api/recruiting'
import { type CandidateRequest, type CandidateSource, candidateSources, type DuplicateMatch } from '../../api/types'
import { QueryError } from '../../app/QueryError'
import { SelectField, TextField } from '../employees/fields'
import { label } from '../employees/format'
import { type StageTarget, stageLabel } from './pipeline'

const useStyles = makeStyles({
  form: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  grid: { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))', gap: tokens.spacingHorizontalM },
  wide: { maxWidth: '760px', width: '100%' },
  match: { display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: tokens.spacingHorizontalM, padding: tokens.spacingHorizontalM },
})

/**
 * CT-03: confirm a move with an optional reason; a rejection needs a reason from the configurable list.
 * Used by drag-and-drop on the board and by the "Move to" menus.
 */
export function MoveDialog({
  applicationId,
  candidateName,
  target,
  onClose,
}: {
  applicationId: string
  candidateName: string
  target: StageTarget
  onClose: () => void
}) {
  const styles = useStyles()
  const move = useMoveStage()
  const rejecting = target.stage === 'Rejected'
  const { data: reasons } = useRejectionReasons(rejecting)
  const [reasonId, setReasonId] = useState('')
  const [reason, setReason] = useState('')
  const apiError = move.error instanceof ApiError ? move.error : undefined

  return (
    <Dialog open onOpenChange={(_, d) => !d.open && onClose()}>
      <DialogSurface>
        <form
          onSubmit={(e) => {
            e.preventDefault()
            move.mutate(
              { id: applicationId, body: { stage: target.stage, round: target.round, rejectionReasonId: reasonId || null, reason: reason.trim() || null } },
              { onSuccess: onClose },
            )
          }}
        >
          <DialogBody>
            <DialogTitle>
              Move {candidateName} to {stageLabel(target.stage, target.round)}
            </DialogTitle>
            <DialogContent className={styles.form}>
              {rejecting && (
                <SelectField
                  label="Rejection reason"
                  required
                  value={reasonId}
                  onChange={setReasonId}
                  options={(reasons ?? []).filter((r) => r.isActive).map((r) => ({ value: r.id, label: r.name }))}
                  error={apiError?.fieldError('rejectionReasonId')}
                />
              )}
              <TextField label={rejecting ? 'Details' : 'Reason'} multiline value={reason} onChange={setReason} hint="Optional; kept in the stage history." />
              {move.error && !apiError?.problem?.errors ? <QueryError error={move.error} /> : null}
              {apiError?.problem?.errors && !apiError.fieldError('rejectionReasonId') && <QueryError error={move.error} />}
            </DialogContent>
            <DialogActions>
              <Button onClick={onClose}>Cancel</Button>
              <Button appearance="primary" type="submit" disabled={move.isPending || (rejecting && !reasonId)}>
                Move
              </Button>
            </DialogActions>
          </DialogBody>
        </form>
      </DialogSurface>
    </Dialog>
  )
}

const empty = {
  givenName: '',
  familyName: '',
  email: '',
  phone: '',
  currentEmployer: '',
  noticePeriodDays: '',
  currentCtc: '',
  expectedCtc: '',
}

/**
 * CT-02/CT-09: add a candidate to a requisition. Email and phone are checked against everyone already in the
 * portal (candidates and employees) as they are typed; a match is linked instead of creating a duplicate person.
 */
export function AddCandidateDialog({ requisitionId, countryCode, onClose }: { requisitionId: string; countryCode: string; onClose: () => void }) {
  const styles = useStyles()
  const add = useAddApplication(requisitionId)
  const { data: countries } = useCountries()
  const [form, setForm] = useState(empty)
  const [source, setSource] = useState<CandidateSource>('Portal')
  const [currency, setCurrency] = useState('')
  const [matches, setMatches] = useState<DuplicateMatch[]>([])
  const currencies = [...new Set((countries ?? []).map((c) => c.currencyCode))]
  const ctcCurrency = currency || countries?.find((c) => c.countryCode === countryCode)?.currencyCode || 'PKR'
  const apiError = add.error instanceof ApiError ? add.error : undefined
  const set = (key: keyof typeof empty, value: string) => setForm((f) => ({ ...f, [key]: value }))

  const lookUp = async () => {
    const email = form.email.trim() || null
    const phone = form.phone.trim() || null
    if (!email && !phone) return setMatches([])
    try {
      setMatches(await checkDuplicates(email, phone))
    } catch {
      // The check is a convenience; the API refuses duplicates on save anyway.
    }
  }

  const amount = (text: string) => (text.trim() ? { amount: Number(text.replace(/,/g, '')) || 0, currency: ctcCurrency } : null)
  const candidate = (): CandidateRequest => ({
    givenName: form.givenName,
    familyName: form.familyName,
    email: form.email.trim() || null,
    phone: form.phone.trim() || null,
    currentEmployer: form.currentEmployer.trim() || null,
    noticePeriodDays: form.noticePeriodDays.trim() ? Number.parseInt(form.noticePeriodDays, 10) : null,
    currentCtc: amount(form.currentCtc),
    expectedCtc: amount(form.expectedCtc),
  })
  const link = (personId: string) => add.mutate({ personId, candidate: null, source }, { onSuccess: onClose })
  const conflicts = apiError?.status === 409 ? (apiError.problem?.duplicates ?? []) : []
  const shown = conflicts.length > 0 ? conflicts : matches

  return (
    <Dialog open onOpenChange={(_, d) => !d.open && onClose()}>
      <DialogSurface className={styles.wide}>
        <form
          onSubmit={(e) => {
            e.preventDefault()
            add.mutate({ personId: null, candidate: candidate(), source }, { onSuccess: onClose })
          }}
        >
          <DialogBody>
            <DialogTitle>Add candidate</DialogTitle>
            <DialogContent className={styles.form}>
              {shown.length > 0 && (
                <MessageBar intent="warning">
                  <MessageBarBody>
                    <MessageBarTitle>Already in the portal</MessageBarTitle>
                    Link the application to the existing person instead of creating a duplicate.
                  </MessageBarBody>
                </MessageBar>
              )}
              {shown.map((m) => (
                <Card key={m.personId} className={styles.match} aria-label={`Existing person ${m.name}`}>
                  <div>
                    <Body1>
                      <strong>{m.name}</strong> {m.isEmployee ? '(employee)' : ''}
                    </Body1>
                    <Caption1>
                      Same {[m.matchedOnEmail && 'email', m.matchedOnPhone && 'phone'].filter(Boolean).join(' and ')}: {m.email ?? ''} {m.phone ?? ''}
                    </Caption1>
                  </div>
                  <Button appearance="primary" disabled={add.isPending} onClick={() => link(m.personId)}>
                    Link {m.name}
                  </Button>
                </Card>
              ))}
              <div className={styles.grid}>
                <TextField label="Given name" required value={form.givenName} onChange={(v) => set('givenName', v)} error={apiError?.fieldError('givenName')} />
                <TextField label="Family name" required value={form.familyName} onChange={(v) => set('familyName', v)} error={apiError?.fieldError('familyName')} />
                <div onBlur={() => void lookUp()}>
                  <TextField label="Email" type="email" value={form.email} onChange={(v) => set('email', v)} error={apiError?.fieldError('email')} />
                </div>
                <div onBlur={() => void lookUp()}>
                  <TextField label="Phone" type="tel" value={form.phone} onChange={(v) => set('phone', v)} error={apiError?.fieldError('phone')} />
                </div>
                <SelectField
                  label="Source"
                  required
                  value={source}
                  onChange={(v) => setSource(v as CandidateSource)}
                  options={candidateSources.map((s) => ({ value: s, label: label(s) }))}
                />
                <TextField label="Current employer" value={form.currentEmployer} onChange={(v) => set('currentEmployer', v)} />
                <TextField label="Notice period (days)" type="number" value={form.noticePeriodDays} onChange={(v) => set('noticePeriodDays', v)} error={apiError?.fieldError('noticePeriodDays')} />
                <SelectField label="Salary currency" value={ctcCurrency} onChange={setCurrency} options={currencies.map((c) => ({ value: c, label: c }))} />
                <TextField label={`Current CTC (${ctcCurrency})`} value={form.currentCtc} onChange={(v) => set('currentCtc', v)} error={apiError?.fieldError('currentCtc')} />
                <TextField label={`Expected CTC (${ctcCurrency})`} value={form.expectedCtc} onChange={(v) => set('expectedCtc', v)} error={apiError?.fieldError('expectedCtc')} />
              </div>
              {add.error && !apiError?.problem?.errors && conflicts.length === 0 ? <QueryError error={add.error} /> : null}
            </DialogContent>
            <DialogActions>
              <Button onClick={onClose}>Cancel</Button>
              <Button appearance="primary" type="submit" disabled={add.isPending}>
                Add as new candidate
              </Button>
            </DialogActions>
          </DialogBody>
        </form>
      </DialogSurface>
    </Dialog>
  )
}
