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
  Menu,
  MenuItem,
  MenuList,
  MenuPopover,
  MenuTrigger,
  SearchBox,
  Spinner,
  Tab,
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableHeaderCell,
  TableRow,
  TabList,
  Text,
  Title2,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components'
import { MoreHorizontalRegular, PersonAddRegular, SettingsRegular } from '@fluentui/react-icons'
import { type DragEvent, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router'
import { ApiError } from '../../api/client'
import { useBoard, useSavePipeline } from '../../api/recruiting'
import { type ApplicationCard, type Board, type CandidateSource, candidateSources, type PipelineStage } from '../../api/types'
import { QueryError } from '../../app/QueryError'
import { SelectField, TextField } from '../employees/fields'
import { label } from '../employees/format'
import { activeColumns, type BoardColumn, daysSince, exitColumns, isExit, moveTargets, stageLabel, type StageTarget, targetKey } from './pipeline'
import { AddCandidateDialog, MoveDialog } from './RecruitingDialogs'

const useStyles = makeStyles({
  page: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL },
  heading: { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: tokens.spacingHorizontalM, flexWrap: 'wrap' },
  actions: { display: 'flex', gap: tokens.spacingHorizontalS, flexWrap: 'wrap' },
  board: { display: 'flex', gap: tokens.spacingHorizontalM, overflowX: 'auto', paddingBottom: tokens.spacingVerticalM, alignItems: 'flex-start' },
  column: {
    flex: '0 0 220px',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    padding: tokens.spacingHorizontalS,
    minHeight: '160px',
    borderRadius: tokens.borderRadiusLarge,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  dropTarget: { outline: `2px dashed ${tokens.colorBrandStroke1}`, backgroundColor: tokens.colorBrandBackground2 },
  columnTitle: { display: 'flex', justifyContent: 'space-between', alignItems: 'center' },
  card: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXXS, padding: tokens.spacingHorizontalS, cursor: 'grab' },
  cardHeader: { display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: tokens.spacingHorizontalXS },
  exits: { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))', gap: tokens.spacingHorizontalM },
  filters: { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))', gap: tokens.spacingHorizontalM, alignItems: 'end' },
  tableWrap: { overflowX: 'auto' },
  form: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
})

type Moving = { card: ApplicationCard; target: StageTarget }

/** CT-01: the pipeline of one requisition as a kanban board (drag-and-drop) or a filterable list. */
export function PipelinePage() {
  const styles = useStyles()
  const { id = '' } = useParams()
  const { data: board, isLoading, error } = useBoard(id)
  const [view, setView] = useState<'board' | 'list'>('board')
  const [adding, setAdding] = useState(false)
  const [settings, setSettings] = useState(false)
  const [moving, setMoving] = useState<Moving | null>(null)

  if (isLoading) return <Spinner label="Loading pipeline…" />
  if (error || !board) return <QueryError error={error} />

  return (
    <section className={styles.page}>
      <Body1>
        <Link to={`/requisitions/${board.requisitionId}`}>Back to {board.number}</Link>
      </Body1>
      <div className={styles.heading}>
        <Title2 as="h1">
          Pipeline: {board.number} {board.title}
        </Title2>
        <div className={styles.actions}>
          {board.canManage && (
            <Button icon={<SettingsRegular />} onClick={() => setSettings(true)}>
              Rounds and scorecard
            </Button>
          )}
          {board.canAddCandidates && (
            <Button appearance="primary" icon={<PersonAddRegular />} onClick={() => setAdding(true)}>
              Add candidate
            </Button>
          )}
        </div>
      </div>
      {!board.canAddCandidates && board.canManage && board.status !== 'Open' && (
        <Body1>Candidates can be added once the requisition is open; it is {label(board.status).toLowerCase()}.</Body1>
      )}

      <TabList selectedValue={view} onTabSelect={(_, d) => setView(d.value as 'board' | 'list')}>
        <Tab value="board">Board</Tab>
        <Tab value="list">List</Tab>
      </TabList>

      {view === 'board' ? (
        <KanbanBoard board={board} onMove={(card, target) => setMoving({ card, target })} />
      ) : (
        <ListView board={board} onMove={(card, target) => setMoving({ card, target })} />
      )}

      {adding && <AddCandidateDialog requisitionId={board.requisitionId} countryCode={board.countryCode} onClose={() => setAdding(false)} />}
      {settings && <PipelineSettingsDialog board={board} onClose={() => setSettings(false)} />}
      {moving && (
        <MoveDialog applicationId={moving.card.id} candidateName={moving.card.candidateName} target={moving.target} onClose={() => setMoving(null)} />
      )}
    </section>
  )
}

function KanbanBoard({ board, onMove }: { board: Board; onMove: (card: ApplicationCard, target: StageTarget) => void }) {
  const styles = useStyles()
  const columns = activeColumns(board.interviewRounds)
  const byKey = useMemo(() => {
    const map = new Map<string, ApplicationCard[]>()
    for (const card of board.applications) {
      const key = targetKey({ stage: card.stage, round: card.interviewRound })
      map.set(key, [...(map.get(key) ?? []), card])
    }
    return map
  }, [board.applications])

  return (
    <>
      <div className={styles.board}>
        {columns.map((column) => (
          <Column key={column.key} board={board} column={column} cards={byKey.get(column.key) ?? []} onMove={onMove} />
        ))}
      </div>
      <Text weight="semibold">Not progressing</Text>
      <div className={styles.exits}>
        {exitColumns.map((column) => (
          <Column key={column.key} board={board} column={column} cards={byKey.get(column.key) ?? []} onMove={onMove} />
        ))}
      </div>
    </>
  )
}

/**
 * A drop zone. Uses the browser's own drag-and-drop (no extra library); every card also has a "Move to" menu so
 * the board works from the keyboard.
 */
function Column({
  board,
  column,
  cards,
  onMove,
}: {
  board: Board
  column: BoardColumn
  cards: ApplicationCard[]
  onMove: (card: ApplicationCard, target: StageTarget) => void
}) {
  const styles = useStyles()
  const [over, setOver] = useState(false)
  const accepts = (e: DragEvent) => board.canManage && e.dataTransfer.types.includes('application/x-hr-application')

  return (
    <div
      className={mergeClasses(styles.column, over && styles.dropTarget)}
      role="region"
      aria-label={`${column.label} column`}
      onDragOver={(e) => {
        if (!accepts(e)) return
        e.preventDefault()
        setOver(true)
      }}
      onDragLeave={() => setOver(false)}
      onDrop={(e) => {
        setOver(false)
        const card = board.applications.find((a) => a.id === e.dataTransfer.getData('application/x-hr-application'))
        if (card && moveTargets(card, board.interviewRounds).some((t) => t.key === column.key)) onMove(card, column)
      }}
    >
      <div className={styles.columnTitle}>
        <Text weight="semibold">{column.label}</Text>
        <Badge appearance="tint" color="informative">
          {cards.length}
        </Badge>
      </div>
      {cards.map((card) => (
        <CandidateCard key={card.id} board={board} card={card} onMove={onMove} />
      ))}
    </div>
  )
}

function CandidateCard({ board, card, onMove }: { board: Board; card: ApplicationCard; onMove: (card: ApplicationCard, target: StageTarget) => void }) {
  const styles = useStyles()
  return (
    <Card
      className={styles.card}
      draggable={board.canManage}
      onDragStart={(e) => {
        e.dataTransfer.setData('application/x-hr-application', card.id)
        e.dataTransfer.effectAllowed = 'move'
      }}
      aria-label={card.candidateName}
    >
      <div className={styles.cardHeader}>
        <Link to={`/applications/${card.id}`}>{card.candidateName}</Link>
        {board.canManage && <MoveMenu board={board} card={card} onMove={onMove} />}
      </div>
      <Caption1>{card.currentEmployer ?? 'No current employer'}</Caption1>
      <Caption1>
        {label(card.source)} · {daysSince(card.stageChangedAtUtc)} days here
        {card.interviewCount > 0 ? ` · ${card.interviewCount} interview${card.interviewCount > 1 ? 's' : ''}` : ''}
      </Caption1>
    </Card>
  )
}

function MoveMenu({ board, card, onMove }: { board: Board; card: ApplicationCard; onMove: (card: ApplicationCard, target: StageTarget) => void }) {
  return (
    <Menu>
      <MenuTrigger disableButtonEnhancement>
        <Button appearance="subtle" size="small" icon={<MoreHorizontalRegular />} aria-label={`Move ${card.candidateName}`} />
      </MenuTrigger>
      <MenuPopover>
        <MenuList>
          {moveTargets(card, board.interviewRounds).map((target) => (
            <MenuItem key={target.key} onClick={() => onMove(card, target)}>
              Move to {target.label}
            </MenuItem>
          ))}
        </MenuList>
      </MenuPopover>
    </Menu>
  )
}

/** CT-01 list view with filters. */
function ListView({ board, onMove }: { board: Board; onMove: (card: ApplicationCard, target: StageTarget) => void }) {
  const styles = useStyles()
  const [search, setSearch] = useState('')
  const [stage, setStage] = useState('')
  const [source, setSource] = useState('')
  const stageOptions = [...activeColumns(board.interviewRounds), ...exitColumns]
  const rows = board.applications.filter((a) => {
    const text = `${a.candidateName} ${a.email ?? ''} ${a.currentEmployer ?? ''}`.toLowerCase()
    return (
      (!search || text.includes(search.toLowerCase())) &&
      (!stage || targetKey({ stage: a.stage, round: a.interviewRound }) === stage) &&
      (!source || a.source === (source as CandidateSource))
    )
  })

  return (
    <>
      <div className={styles.filters}>
        <SearchBox aria-label="Search candidates" placeholder="Name, email or employer" value={search} onChange={(_, d) => setSearch(d.value)} />
        <SelectField label="Stage" value={stage} onChange={setStage} clearLabel="All stages" options={stageOptions.map((c) => ({ value: c.key, label: c.label }))} />
        <SelectField label="Source" value={source} onChange={setSource} clearLabel="All sources" options={candidateSources.map((s) => ({ value: s, label: label(s) }))} />
      </div>
      <div className={styles.tableWrap}>
        <Table aria-label="Candidates">
          <TableHeader>
            <TableRow>
              <TableHeaderCell>Candidate</TableHeaderCell>
              <TableHeaderCell>Stage</TableHeaderCell>
              <TableHeaderCell>Source</TableHeaderCell>
              <TableHeaderCell>Current employer</TableHeaderCell>
              <TableHeaderCell>Days in stage</TableHeaderCell>
              {board.canManage && <TableHeaderCell aria-label="Actions" />}
            </TableRow>
          </TableHeader>
          <TableBody>
            {rows.map((a) => (
              <TableRow key={a.id}>
                <TableCell>
                  <Link to={`/applications/${a.id}`}>{a.candidateName}</Link>
                </TableCell>
                <TableCell>
                  <StageBadge stage={a.stage} round={a.interviewRound} />
                </TableCell>
                <TableCell>{label(a.source)}</TableCell>
                <TableCell>{a.currentEmployer ?? '—'}</TableCell>
                <TableCell>{daysSince(a.stageChangedAtUtc)}</TableCell>
                {board.canManage && (
                  <TableCell>
                    <MoveMenu board={board} card={a} onMove={onMove} />
                  </TableCell>
                )}
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
    </>
  )
}

export function StageBadge({ stage, round }: { stage: PipelineStage; round: number | null }) {
  const color = stage === 'Joined' || stage === 'OfferAccepted' ? 'success' : stage === 'Rejected' ? 'danger' : isExit(stage) ? 'subtle' : 'informative'
  return (
    <Badge appearance="tint" color={color}>
      {stageLabel(stage, round)}
    </Badge>
  )
}

/** HR sets the number of interview rounds and the scorecard criteria (CT-06). */
function PipelineSettingsDialog({ board, onClose }: { board: Board; onClose: () => void }) {
  const styles = useStyles()
  const save = useSavePipeline(board.requisitionId)
  const [rounds, setRounds] = useState(String(board.interviewRounds))
  const [criteria, setCriteria] = useState(board.scorecardCriteria.join('\n'))
  const apiError = save.error instanceof ApiError ? save.error : undefined

  return (
    <Dialog open onOpenChange={(_, d) => !d.open && onClose()}>
      <DialogSurface>
        <form
          onSubmit={(e) => {
            e.preventDefault()
            save.mutate(
              { interviewRounds: Number.parseInt(rounds, 10) || 0, scorecardCriteria: criteria.split('\n').map((c) => c.trim()).filter(Boolean) },
              { onSuccess: onClose },
            )
          }}
        >
          <DialogBody>
            <DialogTitle>Rounds and scorecard</DialogTitle>
            <DialogContent className={styles.form}>
              <TextField label="Interview rounds" type="number" required value={rounds} onChange={setRounds} error={apiError?.fieldError('interviewRounds')} />
              <TextField
                label="Scorecard criteria"
                multiline
                required
                value={criteria}
                onChange={setCriteria}
                hint="One per line; interviewers rate each from 1 to 5. Interviews already scheduled keep their criteria."
                error={apiError?.fieldError('scorecardCriteria')}
              />
              {save.error && !apiError?.problem?.errors ? <QueryError error={save.error} /> : null}
            </DialogContent>
            <DialogActions>
              <Button onClick={onClose}>Cancel</Button>
              <Button appearance="primary" type="submit" disabled={save.isPending}>
                Save
              </Button>
            </DialogActions>
          </DialogBody>
        </form>
      </DialogSurface>
    </Dialog>
  )
}
