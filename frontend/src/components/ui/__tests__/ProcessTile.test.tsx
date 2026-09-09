// SPRINT-140 / TASK-14006 — the shared HR follow-up tile. Pins the one rule
// the whole landing page depends on: colour is licensed by `decisionReady`
// alone, never by the count value, and the §21 window-closed state replaces
// the count row rather than showing a misleading "0".
import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ProcessTile } from '../ProcessTile'

describe('ProcessTile', () => {
  it('renders the count, label and oldest text', () => {
    render(
      <ProcessTile
        title="Bagudrettede rettelser"
        testId="tile-worklist"
        decisionReady={false}
        onOpen={vi.fn()}
        count={3}
        oldestLabel="8 dage"
      />,
    )
    expect(screen.getByText('Bagudrettede rettelser')).toBeInTheDocument()
    expect(screen.getByTestId('tile-worklist-count').textContent).toBe('3')
    expect(screen.getByTestId('tile-worklist-oldest').textContent).toBe('Ældste: 8 dage')
  })

  it('shows colour when decisionReady AND count > 0', () => {
    render(
      <ProcessTile
        title="Godkendt, ikke eksporteret"
        testId="tile-ready"
        decisionReady
        onOpen={vi.fn()}
        count={4}
        oldestLabel="12 dage"
      />,
    )
    expect(screen.getByTestId('tile-ready-tile').dataset.coloured).toBe('true')
  })

  it('never shows colour when decisionReady is false, regardless of count', () => {
    render(
      <ProcessTile
        title="Uden godkenderdaekning"
        testId="tile-notready"
        decisionReady={false}
        onOpen={vi.fn()}
        count={99}
        oldestLabel="40 dage"
      />,
    )
    expect(screen.getByTestId('tile-notready-tile').dataset.coloured).toBe('false')
  })

  it('shows no colour on a decision-ready tile when the count is zero', () => {
    render(
      <ProcessTile
        title="Femte ferieuge"
        testId="tile-empty-ready"
        decisionReady
        onOpen={vi.fn()}
        count={0}
        oldestLabel={null}
      />,
    )
    expect(screen.getByTestId('tile-empty-ready-tile').dataset.coloured).toBe('false')
  })

  it('omits the oldest row when oldestLabel is null (no age dimension, e.g. orphans)', () => {
    render(
      <ProcessTile
        title="Uden godkenderdaekning"
        testId="tile-orphans"
        decisionReady={false}
        onOpen={vi.fn()}
        count={2}
        oldestLabel={null}
      />,
    )
    expect(screen.queryByTestId('tile-orphans-oldest')).toBeNull()
  })

  it('renders a secondary count when supplied', () => {
    render(
      <ProcessTile
        title="Forbi frist"
        testId="tile-past-deadline"
        decisionReady
        onOpen={vi.fn()}
        count={5}
        countLabel="medarbejder forsinket"
        secondary={{ label: 'Godkender forsinket', value: 2 }}
        oldestLabel="3 dage"
      />,
    )
    expect(screen.getByTestId('tile-past-deadline-secondary').textContent).toBe('Godkender forsinket: 2')
  })

  it('the §21 window-closed state replaces the count row entirely — never a "0"', () => {
    render(
      <ProcessTile
        title="§21 femte ferieuge"
        testId="tile-s21"
        decisionReady
        onOpen={vi.fn()}
        stateMessage="Åbner 1. november 2025"
      />,
    )
    expect(screen.getByTestId('tile-s21-state').textContent).toBe('Åbner 1. november 2025')
    expect(screen.queryByTestId('tile-s21-count')).toBeNull()
  })

  it('renders a footnote (e.g. the rolling lookback floor)', () => {
    render(
      <ProcessTile
        title="Godkendt, ikke eksporteret"
        testId="tile-footnote"
        decisionReady
        onOpen={vi.fn()}
        count={1}
        oldestLabel="1 dag"
        footnote="Ser 12 måneder tilbage (fra 2024-09-09)."
      />,
    )
    expect(screen.getByTestId('tile-footnote-footnote').textContent).toContain('12 måneder')
  })

  it('calls onOpen when clicked', async () => {
    const onOpen = vi.fn()
    const user = userEvent.setup()
    render(
      <ProcessTile
        title="Bagudrettede rettelser"
        testId="tile-click"
        decisionReady={false}
        onOpen={onOpen}
        count={1}
        oldestLabel={null}
      />,
    )
    await user.click(screen.getByTestId('tile-click'))
    expect(onOpen).toHaveBeenCalledTimes(1)
  })
})
