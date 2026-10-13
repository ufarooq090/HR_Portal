import { describe, expect, it } from 'vitest'
import { candidatesUrl } from '../../api/recruiting'
import { activeColumns, canMove, daysSince, moveTargets, stageLabel, targetKey } from './pipeline'

describe('CT-01 pipeline board', () => {
  it('has a column per interview round between screening and offer', () => {
    expect(activeColumns(2).map((c) => c.label)).toEqual(['Applied', 'Screening', 'Interview 1', 'Interview 2', 'Offer', 'Offer accepted', 'Joined'])
    expect(activeColumns(3).filter((c) => c.stage === 'Interview')).toHaveLength(3)
  })

  it('keys interview columns by round', () => {
    expect(targetKey({ stage: 'Interview', round: 2 })).toBe('Interview-2')
    expect(targetKey({ stage: 'Offer', round: null })).toBe('Offer')
  })

  it('mirrors the server rules for moves', () => {
    expect(canMove({ stage: 'Screening', round: null }, { stage: 'Joined', round: null })).toBe(false)
    expect(canMove({ stage: 'OfferAccepted', round: null }, { stage: 'Joined', round: null })).toBe(true)
    expect(canMove({ stage: 'Screening', round: null }, { stage: 'OfferAccepted', round: null })).toBe(false)
    expect(canMove({ stage: 'Screening', round: null }, { stage: 'OfferDeclined', round: null })).toBe(false)
    expect(canMove({ stage: 'Interview', round: 1 }, { stage: 'Interview', round: 2 })).toBe(true)
    expect(canMove({ stage: 'Interview', round: 1 }, { stage: 'Interview', round: 1 })).toBe(false)
    expect(canMove({ stage: 'Joined', round: null }, { stage: 'Withdrawn', round: null })).toBe(false)
    expect(canMove({ stage: 'Rejected', round: null }, { stage: 'Screening', round: null })).toBe(true)
  })

  it('offers every legal target except the current place', () => {
    const labels = moveTargets({ stage: 'Applied', interviewRound: null }, 1).map((t) => t.label)
    expect(labels).toEqual(['Screening', 'Interview 1', 'Offer', 'Rejected', 'Withdrawn', 'No-show', 'On hold'])
  })

  it('labels stages for people', () => {
    expect(stageLabel('NoShow')).toBe('No-show')
    expect(stageLabel('Interview', 3)).toBe('Interview 3')
  })

  it('counts whole days in a stage', () => {
    expect(daysSince('2026-10-10T09:00:00Z', new Date('2026-10-13T08:59:00Z'))).toBe(2)
  })

  it('builds the list URL with filters', () => {
    expect(candidatesUrl()).toBe('/api/candidates')
    expect(candidatesUrl({ search: ' sana ', stage: 'Screening', requisitionId: '' })).toBe('/api/candidates?search=sana&stage=Screening')
  })
})
