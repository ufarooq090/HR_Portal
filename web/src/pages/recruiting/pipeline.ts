import { exitStages, type ApplicationCard, type PipelineStage } from '../../api/types'

/** A place on the board: a stage, plus a round for the interview stage. */
export interface StageTarget {
  stage: PipelineStage
  round: number | null
}

export interface BoardColumn extends StageTarget {
  key: string
  label: string
}

export function targetKey(target: StageTarget): string {
  return target.stage === 'Interview' ? `Interview-${target.round}` : target.stage
}

export function stageLabel(stage: PipelineStage, round: number | null = null): string {
  switch (stage) {
    case 'Interview':
      return round ? `Interview ${round}` : 'Interview'
    case 'OfferAccepted':
      return 'Offer accepted'
    case 'OfferDeclined':
      return 'Offer declined'
    case 'NoShow':
      return 'No-show'
    case 'OnHold':
      return 'On hold'
    default:
      return stage
  }
}

/** Applied → Screening → Interview 1..n → Offer → Offer accepted → Joined. */
export function activeColumns(interviewRounds: number): BoardColumn[] {
  const targets: StageTarget[] = [
    { stage: 'Applied', round: null },
    { stage: 'Screening', round: null },
    ...Array.from({ length: interviewRounds }, (_, i) => ({ stage: 'Interview' as const, round: i + 1 })),
    { stage: 'Offer', round: null },
    { stage: 'OfferAccepted', round: null },
    { stage: 'Joined', round: null },
  ]
  return targets.map((t) => ({ ...t, key: targetKey(t), label: stageLabel(t.stage, t.round) }))
}

export const exitColumns: BoardColumn[] = exitStages.map((stage) => ({ stage, round: null, key: stage, label: stageLabel(stage) }))

export function isExit(stage: PipelineStage): boolean {
  return (exitStages as readonly string[]).includes(stage)
}

/** Mirrors Application.MoveTo on the server, so the UI only offers moves the API will accept. */
export function canMove(from: StageTarget, to: StageTarget): boolean {
  if (from.stage === 'Joined') return false
  if (from.stage === to.stage && from.round === to.round) return false
  if (to.stage === 'Joined') return from.stage === 'OfferAccepted'
  if (to.stage === 'OfferAccepted') return from.stage === 'Offer'
  if (to.stage === 'OfferDeclined') return from.stage === 'Offer' || from.stage === 'OfferAccepted'
  return true
}

export function moveTargets(card: Pick<ApplicationCard, 'stage' | 'interviewRound'>, interviewRounds: number): BoardColumn[] {
  const from = { stage: card.stage, round: card.interviewRound }
  return [...activeColumns(interviewRounds), ...exitColumns].filter((c) => canMove(from, c))
}

export function daysSince(iso: string, now = new Date()): number {
  return Math.max(0, Math.floor((now.getTime() - new Date(iso).getTime()) / 86_400_000))
}
