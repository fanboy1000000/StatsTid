// S72 / TASK-7203 — component-level tests for the Skema day panel (handoff
// README §4 / prototype solA.jsx DayPanel inside the kit Drawer). The panel is
// CONTROLLED (R16): fixtures in via props, mutations out via callbacks — the
// end-to-end save/round-trip pins land in 7205 (R13 layering). Fixture Maps are
// created once per test and passed by reference (PAT-007).
//
// March 2026 reference: Mar 1 = Sunday, Mar 9 = Monday.
import { render, fireEvent, screen } from '@testing-library/react'
import { SkemaDayPanel, type DayPanelPeriod } from '../SkemaDayPanel'
import { analyzeRestPeriods, RestZoneUnavailableError } from '../../hooks/useSkema'
import type { WorkTimeDay } from '../../types'

// The zone-notice arm overrides `analyzeRestPeriods` for one test; the wrapper keeps the real
// implementation for every other test (a module mock reaches only what the panel imports).
vi.mock('../../hooks/useSkema', async (importOriginal) => {
  const original = await importOriginal<typeof import('../../hooks/useSkema')>()
  return { ...original, analyzeRestPeriods: vi.fn(original.analyzeRestPeriods) }
})

const PROJECTS = [
  { key: 'PRJ-A', label: 'Projekt Alfa' },
  { key: 'PRJ-B', label: 'Projekt Beta' },
]

// The served minimum rest, 11 h on every day of the March the tests use (value-neutral for the
// warnings asserted below; the 12 h arm overrides it).
const served = (hours: number | null) =>
  Array.from({ length: 31 }, (_, i) => ({ date: `2026-03-${String(i + 1).padStart(2, '0')}`, hours }))

const per = (id: string, from: string, to: string): DayPanelPeriod => ({ id, from, to })

type PanelProps = Parameters<typeof SkemaDayPanel>[0]

function renderPanel(overrides: Partial<PanelProps> = {}) {
  const props: PanelProps = {
    open: true,
    date: '2026-03-09', // Monday
    periods: [],
    projectRows: PROJECTS,
    allocations: new Map<string, number>(),
    dailyNorm: 7.4,
    monthWorkTime: [],
    boundaryWorkTime: [],
    minimumRest: served(11),
    onPeriodsChange: vi.fn(),
    onAllocationChange: vi.fn(),
    onClose: vi.fn(),
    onOpenManager: vi.fn(),
    ...overrides,
  }
  return { ...render(<SkemaDayPanel {...props} />), props }
}

afterEach(() => {
  document.body.style.overflow = ''
})

describe('SkemaDayPanel — header + meta bar', () => {
  it('renders inside the kit Drawer with the date-labelled dialog name', () => {
    renderPanel()
    expect(screen.getByRole('dialog')).toHaveAccessibleName('Registrér tid — mandag 9. marts')
  })

  it('renders the eyebrow "Registrér tid", the long Danish date title, and the ✕ close button', () => {
    const { props } = renderPanel()
    expect(screen.getByText('Registrér tid')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'mandag 9. marts' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Luk' }))
    expect(props.onClose).toHaveBeenCalledTimes(1)
  })

  it('meta bar shows the Diff. fra normtid from the served norm and the worked periods, plus "norm X,X t"', () => {
    renderPanel({ periods: [per('p1', '08:00', '16:00')] }) // 8h vs norm 7.4
    expect(screen.getByText('Diff. fra normtid')).toBeInTheDocument()
    const value = screen.getByText('+0,6 t')
    expect(value.className).toContain('diffPos')
    expect(screen.getByText('norm 7,4 t')).toBeInTheDocument()
  })

  it('meta bar shows a red negative diff when worked < norm', () => {
    renderPanel({ periods: [per('p1', '08:00', '12:00')] }) // 4h vs 7.4
    const value = screen.getByText('-3,4 t')
    expect(value.className).toContain('diffNeg')
  })

  it('meta bar shows — when nothing is worked yet', () => {
    renderPanel()
    expect(screen.getByText('—')).toBeInTheDocument()
  })

  it('R1: a null served norm renders the meta blank (diff — / norm —)', () => {
    renderPanel({ dailyNorm: null, periods: [per('p1', '08:00', '16:00')] })
    expect(screen.getByText('—')).toBeInTheDocument() // diff cannot compute
    expect(screen.getByText(/^norm/).textContent).toBe('norm —')
  })
})

describe('SkemaDayPanel — R6 warnings (warning-ONLY, panel meta area)', () => {
  it('R6: the >9t intra-day warning renders an amber merarbejde/hvile Alert when worked > 9 t', () => {
    renderPanel({ periods: [per('p1', '08:00', '17:30')] }) // 9.5h
    const alert = screen.getByText(/Mere end 9 timers arbejde på én dag kan udløse merarbejde/)
    expect(alert).toBeInTheDocument()
    expect(alert.closest('[role="alert"]')?.className).toContain('warning')
  })

  it('R6: exactly 9,0 worked hours does NOT trigger the intra-day warning', () => {
    renderPanel({ periods: [per('p1', '08:00', '17:00')] }) // 9.0h
    expect(screen.queryByText(/Mere end 9 timers arbejde/)).toBeNull()
  })

  it('R6: more than 9 clock hours triggers the intra-day warning', () => {
    renderPanel({ periods: [per('p1', '08:00', '18:00')] }) // 10.0h
    expect(screen.getByText(/Mere end 9 timers arbejde/)).toBeInTheDocument()
  })

  // S148 3b (RR-1, AC-S0-4f): the warning names the judged work end, not two days. Kind F. Red
  // conditions: the text says "giver kun 8 timers hvile mellem søndag 8. marts og mandag 9. marts".
  it('R6: the 24-hour look-back analysis warns from the served month workTime, LIVE against the panel periods', () => {
    const monthWorkTime: WorkTimeDay[] = [
      { date: '2026-03-08', intervals: [{ start: '12:00', end: '23:00' }] },
    ]
    renderPanel({ monthWorkTime, periods: [per('p1', '07:00', '15:00')] }) // 8h rest before 15:00
    const warning = screen.getByText(/giver kun 8 timers sammenhængende hvile/)
    expect(warning.textContent).toBe(
      'Denne registrering giver kun 8 timers sammenhængende hvile i de 24 timer før arbejdet slutter mandag 9. marts kl. 15:00. ' +
        'Hvile under 11 timer registreres som et hviletidsbrud, der er synligt for din leder og kan udløse kompenserende hvile.',
    )
    expect(warning.closest('[role="alert"]')?.className).toContain('warning')
  })

  // Kind F (S148 TASK-14829, ruling 24). Red conditions: the sentence names "11" whatever the served
  // minimum is; the long-day advisory names "11" too, and keeps naming it with no served value.
  it('S148: the warning and the long-day advisory name the served minimum, 12 h', () => {
    const monthWorkTime: WorkTimeDay[] = [
      { date: '2026-03-08', intervals: [{ start: '12:00', end: '23:00' }] },
    ]
    renderPanel({ monthWorkTime, periods: [per('p1', '07:00', '17:00')], minimumRest: served(12) }) // 8 h rest, 10 h day
    expect(screen.getByText(/giver kun 8 timers sammenhængende hvile/).textContent).toContain('Hvile under 12 timer registreres')
    expect(screen.getByText(/Mere end 9 timers arbejde/).textContent).toBe(
      'Mere end 9 timers arbejde på én dag kan udløse merarbejde. Husk 12 timers hvile til næste vagt.',
    )
  })

  // Kind R (S148 FU-3, T18): today's code renders "11,5" too (it re-reads the served map), so this
  // arm is green before the change and pins the number. Red condition: any rendering of the
  // threshold other than the served 11.5 in Danish form, e.g. "12" (rounded) or "11" (truncated).
  it('S148 FU-3: a served 11.5 h minimum is named "Hvile under 11,5 timer"', () => {
    const monthWorkTime: WorkTimeDay[] = [
      { date: '2026-03-08', intervals: [{ start: '12:00', end: '23:00' }] },
    ]
    renderPanel({ monthWorkTime, periods: [per('p1', '07:00', '17:00')], minimumRest: served(11.5) })
    expect(screen.getByText(/giver kun 8 timers sammenhængende hvile/).textContent).toContain('Hvile under 11,5 timer registreres')
  })

  // Kind F (S148 FU-3, T18). Red condition: a finding that carries no served minimum (the analysis
  // is stubbed to return one, the served list having no value for its date) renders
  // "Hvile under 0 timer" today, because the panel falls back to 0; it must render no threshold sentence.
  it('S148 FU-3: a finding without a served minimum renders no threshold sentence and no "0 timer"', () => {
    const mocked = vi.mocked(analyzeRestPeriods)
    const real = mocked.getMockImplementation()
    mocked.mockImplementation(
      () => [{ date: '2026-03-09', endsAt: '17:00', restSeconds: 8 * 3600 }] as unknown as ReturnType<typeof analyzeRestPeriods>,
    )
    try {
      renderPanel({ periods: [per('p1', '07:00', '17:00')], minimumRest: [] })
      const text = screen.getByText(/giver kun 8 timers sammenhængende hvile/).textContent ?? ''
      expect(text).not.toMatch(/Hvile under/)
      expect(text).not.toMatch(/0 timer/)
    } finally {
      mocked.mockImplementation(real!)
    }
  })

  it('S148: with no served minimum for the open date the advisory is its first sentence only', () => {
    renderPanel({ periods: [per('p1', '08:00', '18:00')], minimumRest: served(null) })
    expect(screen.getByText(/Mere end 9 timers arbejde/).textContent).toBe(
      'Mere end 9 timers arbejde på én dag kan udløse merarbejde.',
    )
  })

  // Kind F (S148 3b, adjudication item 9). Red conditions: the shown rest is rounded half up,
  // "8,5". A rest of 8 h 27 min (8,45) is shown rounded down, "8,4", as the server shows it.
  it('S148: a rest of 8 h 27 min is shown rounded down, "8,4 timers sammenhængende hvile"', () => {
    const monthWorkTime: WorkTimeDay[] = [
      { date: '2026-03-08', intervals: [{ start: '14:00', end: '22:00' }] },
    ]
    renderPanel({ monthWorkTime, periods: [per('p1', '06:27', '14:00')] })
    expect(screen.getByText(/giver kun 8,4 timers sammenhængende hvile/)).toBeInTheDocument()
  })

  // Kind F (AC-S0-4e). Red conditions: 8 h 29 min 50 s rounds to two decimals first (8,49) and then
  // up to 8,5; rounded once and down it is 8,4. The panel shows the typed seconds' figure (the save
  // keeps hours and minutes, `toHHmm`).
  it('S148: a typed rest of 8 h 29 min 50 s is shown once and rounded down, "8,4"', () => {
    const monthWorkTime: WorkTimeDay[] = [
      { date: '2026-03-08', intervals: [{ start: '14:00', end: '22:00' }] },
    ]
    renderPanel({ monthWorkTime, periods: [per('p1', '06:29:50', '14:00')] })
    expect(screen.getByText(/giver kun 8,4 timers sammenhængende hvile/)).toBeInTheDocument()
  })

  // Kind F. Red conditions: the old pair rule finds no pair in one 23-hour day and shows no rest
  // warning. When the shown rest is exactly 1 the copy reads "1 times" (adjudication item 17).
  it('S148: a 23-hour day shows "giver kun 1 times sammenhængende hvile" and names the judged end', () => {
    renderPanel({ periods: [per('p1', '00:30', '23:30')] })
    expect(
      screen.getByText(
        /giver kun 1 times sammenhængende hvile i de 24 timer før arbejdet slutter mandag 9\. marts kl\. 23:30/,
      ),
    ).toBeInTheDocument()
  })

  // Kind F. Red conditions: today the panel neither catches the zone error nor shows a notice.
  it('S148: a browser without Danish time-zone data shows one informational line instead of a rest warning', () => {
    const monthWorkTime: WorkTimeDay[] = [
      { date: '2026-03-08', intervals: [{ start: '12:00', end: '23:00' }] },
    ]
    const mocked = vi.mocked(analyzeRestPeriods)
    const real = mocked.getMockImplementation()
    mocked.mockImplementation(() => {
      throw new RestZoneUnavailableError()
    })
    try {
      renderPanel({ monthWorkTime, periods: [per('p1', '07:00', '15:00')] })
      const notice = screen.getByText(
        'Hviletiden kan ikke kontrolleres i denne browser, fordi tidszonedata for Danmark mangler.',
      )
      expect(notice.closest('[role="alert"]')?.className).toContain('info')
      expect(screen.queryByText(/sammenhængende hvile/)).toBeNull()
    } finally {
      mocked.mockImplementation(real!)
    }
  })

  it('R6: the look-back analysis uses the served boundaryWorkTime at the month edge', () => {
    const boundaryWorkTime: WorkTimeDay[] = [
      { date: '2026-02-28', intervals: [{ start: '14:00', end: '23:30' }] },
    ]
    renderPanel({
      date: '2026-03-01',
      boundaryWorkTime,
      periods: [per('p1', '06:00', '10:00')], // 6.5h rest from Feb 28's 23:30 end to 06:00
    })
    expect(
      screen.getByText(
        /giver kun 6,5 timers sammenhængende hvile i de 24 timer før arbejdet slutter søndag 1\. marts kl\. 10:00/,
      ),
    ).toBeInTheDocument()
  })

  it('R6: there is NO VoluntaryUnsocialHours toggle in the warning context (recorded follow-up — warning-only)', () => {
    renderPanel({ periods: [per('p1', '08:00', '17:30')] })
    expect(screen.queryByRole('checkbox')).toBeNull()
    expect(screen.queryByText(/frivillig/i)).toBeNull()
  })
})

describe('SkemaDayPanel — step 1: Registrér arbejdsperioder', () => {
  it('renders fra–til inputs with a per-row "= X,X t" result at 1-decimal display', () => {
    renderPanel({ periods: [per('p1', '10:27', '13:03')] })
    expect(screen.getByLabelText('Fra')).toHaveValue('10:27')
    expect(screen.getByLabelText('Til')).toHaveValue('13:03')
    expect(screen.getByText('= 2,6 t')).toBeInTheDocument()
  })

  it('an empty day renders ONE blank period row without mutating anything', () => {
    const { props } = renderPanel()
    expect(screen.getByLabelText('Fra')).toHaveValue('')
    expect(screen.getByLabelText('Til')).toHaveValue('')
    expect(props.onPeriodsChange).not.toHaveBeenCalled()
  })

  it('editing a fra input propagates the FULL period list via onPeriodsChange(date, periods)', () => {
    const { props } = renderPanel({ periods: [per('p1', '08:00', '12:00')] })
    fireEvent.change(screen.getByLabelText('Fra'), { target: { value: '09:00' } })
    expect(props.onPeriodsChange).toHaveBeenCalledWith('2026-03-09', [
      { id: 'p1', from: '09:00', to: '12:00' },
    ])
  })

  it('+ Tilføj periode appends a new empty row', () => {
    const { props } = renderPanel({ periods: [per('p1', '08:00', '12:00')] })
    fireEvent.click(screen.getByRole('button', { name: '+ Tilføj periode' }))
    expect(props.onPeriodsChange).toHaveBeenCalledTimes(1)
    const arg = (props.onPeriodsChange as ReturnType<typeof vi.fn>).mock.calls[0][1] as DayPanelPeriod[]
    expect(arg).toHaveLength(2)
    expect(arg[0]).toEqual({ id: 'p1', from: '08:00', to: '12:00' })
    expect(arg[1].from).toBe('')
    expect(arg[1].to).toBe('')
  })

  it('the remove button removes its row — and is hidden when only one row remains', () => {
    const { props, rerender } = renderPanel({
      periods: [per('p1', '08:00', '12:00'), per('p2', '13:00', '16:00')],
    })
    const removeButtons = screen.getAllByRole('button', { name: 'Fjern periode' })
    expect(removeButtons).toHaveLength(2)
    fireEvent.click(removeButtons[0])
    expect(props.onPeriodsChange).toHaveBeenCalledWith('2026-03-09', [
      { id: 'p2', from: '13:00', to: '16:00' },
    ])
    rerender(<SkemaDayPanel {...props} periods={[per('p2', '13:00', '16:00')]} />)
    expect(screen.queryByRole('button', { name: 'Fjern periode' })).toBeNull()
  })

  it('R11: invalid/reversed periods show "ugyldig" inline (with the error input state)', () => {
    renderPanel({ periods: [per('p1', '13:00', '10:00')] }) // reversed
    expect(screen.getByText('ugyldig')).toBeInTheDocument()
    expect(screen.getByLabelText('Fra')).toHaveAttribute('aria-invalid', 'true')
  })

  it('a partially-typed period is flagged "ugyldig" until it parses (prototype behavior), while a fully blank row is quiet', () => {
    const { rerender, props } = renderPanel({ periods: [per('p1', '08:0', '')] }) // unparsable
    expect(screen.getByText('ugyldig')).toBeInTheDocument()
    rerender(<SkemaDayPanel {...props} periods={[per('p1', '', '')]} />)
    expect(screen.queryByText('ugyldig')).toBeNull()
  })

  it('Arbejdstid i alt sums the valid periods at 1-decimal display', () => {
    renderPanel({ periods: [per('p1', '08:00', '12:00'), per('p2', '12:30', '16:00')] }) // 4 + 3.5
    const totalRow = screen.getByText('Arbejdstid i alt').parentElement as HTMLElement
    expect(totalRow.textContent).toBe('Arbejdstid i alt7,5 t')
  })

  it('S58 mirror: overlapping periods are flagged "ugyldig" on BOTH rows with the advisory message (backend 422 stays authoritative)', () => {
    renderPanel({ periods: [per('p1', '08:00', '12:00'), per('p2', '11:00', '15:00')] })
    expect(screen.getAllByText('ugyldig')).toHaveLength(2)
    expect(screen.getByText('Arbejdsperioderne overlapper hinanden.')).toBeInTheDocument()
  })

  it('S58 mirror: touching boundaries (12:00–12:00) are NOT an overlap', () => {
    renderPanel({ periods: [per('p1', '08:00', '12:00'), per('p2', '12:00', '16:00')] })
    expect(screen.queryByText('ugyldig')).toBeNull()
    expect(screen.queryByText(/overlapper/)).toBeNull()
  })

  it('S58 mirror: a day total over 24h shows the advisory inline (the panel sums overlapping periods)', () => {
    renderPanel({ periods: [per('p1', '00:00', '23:00'), per('p2', '00:00', '02:00')] }) // 23 + 2 = 25
    expect(
      screen.getByText('Arbejdstid må ikke overstige 24 timer (i alt 25 t).'),
    ).toBeInTheDocument()
  })

})

describe('SkemaDayPanel — step 2: Fordel på projekter (R11 + R5)', () => {
  it('R11: step 2 renders DISABLED (dimmed, inputs disabled) until step 1 has hours', () => {
    const { rerender, props } = renderPanel()
    expect(screen.getByLabelText('Projekt Alfa')).toBeDisabled()
    expect(screen.getByLabelText('Projekt Beta')).toBeDisabled()
    expect(document.querySelector('.sectionDim')).not.toBeNull()

    rerender(<SkemaDayPanel {...props} periods={[per('p1', '08:00', '12:00')]} />)
    expect(screen.getByLabelText('Projekt Alfa')).toBeEnabled()
    expect(screen.getByLabelText('Projekt Beta')).toBeEnabled()
    expect(document.querySelector('.sectionDim')).toBeNull()
  })

  it('renders one FIXED row per visible project (label + input + t) — no add/remove controls', () => {
    renderPanel({ periods: [per('p1', '08:00', '16:00')] })
    expect(screen.getByText('Projekt Alfa')).toBeInTheDocument()
    expect(screen.getByText('Projekt Beta')).toBeInTheDocument()
    expect(screen.getByLabelText('Projekt Alfa')).toBeInTheDocument()
    expect(screen.getByLabelText('Projekt Beta')).toBeInTheDocument()
    // No per-row remove and no project add inside step 2 (membership = manager modal).
    expect(screen.queryByRole('button', { name: /Fjern$/ })).toBeNull()
    expect(screen.queryByRole('button', { name: /Tilføj projekt/ })).toBeNull()
  })

  it('R11: the step-2 header carries the "Administrer projekter" link → onOpenManager', () => {
    const { props } = renderPanel({ periods: [per('p1', '08:00', '16:00')] })
    fireEvent.click(screen.getByRole('button', { name: 'Administrer projekter' }))
    expect(props.onOpenManager).toHaveBeenCalledTimes(1)
  })

  it('typing in a project row propagates onAllocationChange with the parsed exact value (Danish comma)', () => {
    const { props } = renderPanel({ periods: [per('p1', '08:00', '16:00')] })
    fireEvent.change(screen.getByLabelText('Projekt Alfa'), { target: { value: '3,5' } })
    expect(props.onAllocationChange).toHaveBeenCalledWith('2026-03-09', 'PRJ-A', 3.5)
  })

  it('S145 TASK-14516: a negative allocation (-3,5) is refused at entry — no onAllocationChange, flagged with an alert', () => {
    const { props } = renderPanel({ periods: [per('p1', '08:00', '16:00')] })
    const input = screen.getByLabelText('Projekt Alfa')
    fireEvent.change(input, { target: { value: '-3,5' } })
    expect(props.onAllocationChange).not.toHaveBeenCalled()
    expect(input.getAttribute('aria-invalid')).toBe('true')
    expect(screen.getByRole('alert').textContent).toBe('Timer kan ikke være negative')
  })

  it('S145 TASK-14516 recovery: -3,5 then 3,5 → exactly one onAllocationChange(3.5), alert gone', () => {
    const { props } = renderPanel({ periods: [per('p1', '08:00', '16:00')] })
    const input = screen.getByLabelText('Projekt Alfa')
    fireEvent.change(input, { target: { value: '-3,5' } })
    expect(screen.getByRole('alert')).toBeTruthy()
    fireEvent.change(input, { target: { value: '3,5' } })
    expect(props.onAllocationChange).toHaveBeenCalledTimes(1)
    expect(props.onAllocationChange).toHaveBeenCalledWith('2026-03-09', 'PRJ-A', 3.5)
    expect(input.getAttribute('aria-invalid')).toBeNull()
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('S145 TASK-14516: blur clears the negative-allocation flag', () => {
    renderPanel({ periods: [per('p1', '08:00', '16:00')] })
    const input = screen.getByLabelText('Projekt Alfa')
    fireEvent.focus(input)
    fireEvent.change(input, { target: { value: '-3,5' } })
    expect(screen.getByRole('alert')).toBeTruthy()
    fireEvent.blur(input)
    expect(screen.queryByRole('alert')).toBeNull()
    expect(input.getAttribute('aria-invalid')).toBeNull()
  })

  it('S145 TASK-14516: -0 in an allocation is not flagged and propagates null', () => {
    const { props } = renderPanel({ periods: [per('p1', '08:00', '16:00')] })
    const input = screen.getByLabelText('Projekt Alfa')
    fireEvent.change(input, { target: { value: '-0' } })
    expect(props.onAllocationChange).toHaveBeenCalledWith('2026-03-09', 'PRJ-A', null)
    expect(input.getAttribute('aria-invalid')).toBeNull()
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('clearing a project row propagates null (the page posts it as a removal)', () => {
    const allocations = new Map([['PRJ-A', 4]])
    const { props } = renderPanel({ periods: [per('p1', '08:00', '16:00')], allocations })
    fireEvent.change(screen.getByLabelText('Projekt Alfa'), { target: { value: '' } })
    expect(props.onAllocationChange).toHaveBeenCalledWith('2026-03-09', 'PRJ-A', null)
  })

  it('Resterende at fordele: AMBER with the remainder when hours remain unallocated', () => {
    const allocations = new Map([['PRJ-A', 5]])
    renderPanel({ periods: [per('p1', '08:00', '15:30')], allocations }) // 7.5 − 5 = 2.5
    expect(screen.getByText('Resterende at fordele')).toBeInTheDocument()
    expect(screen.getByText('2,5 t')).toBeInTheDocument()
    expect(screen.getByText('Resterende at fordele').parentElement?.className).toContain('remainingLeft')
  })

  it('Resterende: GREEN "Alt fordelt ✓" when balanced', () => {
    const allocations = new Map([
      ['PRJ-A', 5],
      ['PRJ-B', 2.5],
    ])
    renderPanel({ periods: [per('p1', '08:00', '15:30')], allocations })
    expect(screen.getByText('Alt fordelt ✓')).toBeInTheDocument()
    expect(screen.getByText('Alt fordelt ✓').parentElement?.className).toContain('remainingOk')
  })

  it('Resterende: RED "Overfordelt" with the excess when over-allocated', () => {
    const allocations = new Map([['PRJ-A', 9]])
    renderPanel({ periods: [per('p1', '08:00', '15:30')], allocations }) // 7.5 − 9 = −1.5
    expect(screen.getByText('Overfordelt')).toBeInTheDocument()
    expect(screen.getByText('+1,5 t')).toBeInTheDocument()
    expect(screen.getByText('Overfordelt').parentElement?.className).toContain('remainingOver')
  })

  it('R5: the Alt fordelt mirror uses the gate tolerance — a raw Δ of 0,0049 is balanced, 0,0051 is not (0.005 @ 2 decimals)', () => {
    // worked = 7.5 exactly. 7.4951 rounds to 7.50 → |Δ| = 0 < 0.005 → balanced;
    // 7.4949 rounds to 7.49 → |Δ| = 0.01 ≥ 0.005 → under (amber).
    const balanced = new Map([['PRJ-A', 7.4951]])
    const { rerender, props } = renderPanel({ periods: [per('p1', '08:00', '15:30')], allocations: balanced })
    expect(screen.getByText('Alt fordelt ✓')).toBeInTheDocument()

    const under = new Map([['PRJ-A', 7.4949]])
    rerender(<SkemaDayPanel {...props} allocations={under} />)
    expect(screen.queryByText('Alt fordelt ✓')).toBeNull()
    expect(screen.getByText('Resterende at fordele')).toBeInTheDocument()
  })

  it('R5 odd-minute pin: display rounds to 0,1 but the mirror compares the EXACT 2-decimal value (08:00–11:47 = 3,78)', () => {
    // Display: "= 3,8 t". Exact: 3.78 — allocating the DISPLAYED 3.8 must NOT
    // read as balanced (Δ = 0.02), while allocating the exact 3.78 must.
    const exact = new Map([['PRJ-A', 3.78]])
    const { rerender, props } = renderPanel({ periods: [per('p1', '08:00', '11:47')], allocations: exact })
    expect(screen.getByText('= 3,8 t')).toBeInTheDocument()
    expect(screen.getByText('Alt fordelt ✓')).toBeInTheDocument()

    const displayValue = new Map([['PRJ-A', 3.8]])
    rerender(<SkemaDayPanel {...props} allocations={displayValue} />)
    expect(screen.queryByText('Alt fordelt ✓')).toBeNull()
    expect(screen.getByText('Overfordelt')).toBeInTheDocument()
  })

  it('R3: the Resterende computes over ALL served allocations — including projects hidden from the visible rows', () => {
    const allocations = new Map([
      ['PRJ-A', 4],
      ['HIDDEN-PROJ', 3.5], // not among projectRows — still counts (grid agreement)
    ])
    renderPanel({ periods: [per('p1', '08:00', '15:30')], allocations }) // 7.5 allocated in total
    expect(screen.getByText('Alt fordelt ✓')).toBeInTheDocument()
  })
})

describe('SkemaDayPanel — R11 panel shape + footer', () => {
  it('R11: Færdig closes the panel', () => {
    const { props } = renderPanel()
    fireEvent.click(screen.getByRole('button', { name: 'Færdig' }))
    expect(props.onClose).toHaveBeenCalledTimes(1)
  })

  it('R11: the panel has NO absence section and NO clock-in/out — only the two steps', () => {
    renderPanel({ periods: [per('p1', '08:00', '16:00')] })
    expect(screen.getByText('Registrér arbejdsperioder')).toBeInTheDocument()
    expect(screen.getByText('Fordel på projekter')).toBeInTheDocument()
    expect(screen.queryByText(/fravær/i)).toBeNull()
    expect(screen.queryByText(/ferie/i)).toBeNull()
    expect(screen.queryByText(/stempl|komme|klok ind/i)).toBeNull()
  })
})
