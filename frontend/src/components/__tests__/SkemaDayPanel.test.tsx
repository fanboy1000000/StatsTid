// S72 / TASK-7203 — component-level tests for the Skema day panel (handoff
// README §4 / prototype solA.jsx DayPanel inside the kit Drawer). The panel is
// CONTROLLED (R16): fixtures in via props, mutations out via callbacks — the
// end-to-end save/round-trip pins land in 7205 (R13 layering). Fixture Maps are
// created once per test and passed by reference (PAT-007).
//
// March 2026 reference: Mar 1 = Sunday, Mar 9 = Monday.
import { render, fireEvent, screen } from '@testing-library/react'
import { SkemaDayPanel, type DayPanelPeriod } from '../SkemaDayPanel'
import type { WorkTimeDay } from '../../types'

const PROJECTS = [
  { key: 'PRJ-A', label: 'Projekt Alfa' },
  { key: 'PRJ-B', label: 'Projekt Beta' },
]

const per = (id: string, from: string, to: string): DayPanelPeriod => ({ id, from, to })

type PanelProps = Parameters<typeof SkemaDayPanel>[0]

function renderPanel(overrides: Partial<PanelProps> = {}) {
  const props: PanelProps = {
    open: true,
    date: '2026-03-09', // Monday
    periods: [],
    manualHours: 0,
    projectRows: PROJECTS,
    allocations: new Map<string, number>(),
    dailyNorm: 7.4,
    monthWorkTime: [],
    boundaryWorkTime: [],
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

  it('R6: manual hours count toward the >9t trigger (D-B: worked totals include them)', () => {
    renderPanel({ periods: [per('p1', '08:00', '16:00')], manualHours: 2 }) // 8 + 2 = 10
    expect(screen.getByText(/Mere end 9 timers arbejde/)).toBeInTheDocument()
  })

  it('R6: the adjacent-day 11-hour analysis warns from the served month workTime, LIVE against the panel periods', () => {
    const monthWorkTime: WorkTimeDay[] = [
      { date: '2026-03-08', intervals: [{ start: '12:00', end: '23:00' }], manualHours: 0 },
    ]
    renderPanel({ monthWorkTime, periods: [per('p1', '07:00', '15:00')] }) // gap 8h
    const warning = screen.getByText(/giver kun 8 timers hvile/)
    expect(warning.textContent).toContain('søndag 8. marts')
    expect(warning.textContent).toContain('mandag 9. marts')
    expect(warning.textContent).toContain('kompenserende hvile')
    expect(warning.closest('[role="alert"]')?.className).toContain('warning')
  })

  it('R6: the adjacent-day analysis uses the served boundaryWorkTime at the month edge', () => {
    const boundaryWorkTime: WorkTimeDay[] = [
      { date: '2026-02-28', intervals: [{ start: '14:00', end: '23:30' }], manualHours: 0 },
    ]
    renderPanel({
      date: '2026-03-01',
      boundaryWorkTime,
      periods: [per('p1', '06:00', '10:00')], // gap 6.5h to Feb 28's 23:30 end
    })
    expect(screen.getByText(/giver kun 6,5 timers hvile/)).toBeInTheDocument()
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

  it('S58 mirror: a day total over 24h shows the advisory inline (manual hours included)', () => {
    renderPanel({ periods: [per('p1', '00:00', '23:00')], manualHours: 2 }) // 23 + 2 = 25
    expect(
      screen.getByText('Arbejdstid må ikke overstige 24 timer (i alt 25 t).'),
    ).toBeInTheDocument()
  })

  it('D-B: a day with existing manualHours renders the read-only "Manuelt registreret: X t" line — and NO entry UI', () => {
    renderPanel({ manualHours: 2 })
    expect(screen.getByText('Manuelt registreret: 2 t')).toBeInTheDocument()
    // The only textboxes are the period fra/til pair + the two project rows —
    // no manual-hours input exists anywhere in the panel.
    expect(screen.getAllByRole('textbox')).toHaveLength(4)
  })

  it('D-B: the manual line is absent when the day has no manual hours', () => {
    renderPanel({ manualHours: 0 })
    expect(screen.queryByText(/Manuelt registreret/)).toBeNull()
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

  it('a manual-only legacy day still enables step 2 (manual hours count in the worked total)', () => {
    renderPanel({ manualHours: 7.4 })
    expect(screen.getByLabelText('Projekt Alfa')).toBeEnabled()
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

  it('clearing a project row propagates null (the save path drops null/0 — R17 recorded limitation)', () => {
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
