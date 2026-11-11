// TASK-14810 / AC-S0-5c — the day panel shows no manual-hours line. Kind F (vitest).
// Manual hours are retired (ADR-028, ADR-043 D10); the panel's total is the clock periods alone.
// Fact (i) is the F assertion: on the pre-change source the panel carries the read-only
// "Manuelt registreret" line, so the source pin fails there. Fact (ii) holds before and after.
//
// This file does not name the retired member: a test that fed it would have to spell it, and
// AC-S0-5a requires that spelling nowhere under frontend/src.
import { render, screen } from '@testing-library/react'
import { SkemaDayPanel } from '../SkemaDayPanel'
import skemaDayPanelSource from '../SkemaDayPanel.tsx?raw'

describe('SkemaDayPanel — no manual-hours line (AC-S0-5c)', () => {
  it('(i) the panel source contains no "Manuelt registreret" text', () => {
    expect(skemaDayPanelSource).not.toContain('Manuelt registreret')
  })

  it('(ii) a 08:00–16:00 day on Monday 9 March 2026 renders no "Manuelt registreret" and "Arbejdstid i alt" reads 8 t', () => {
    render(
      <SkemaDayPanel
        open
        date="2026-03-09"
        periods={[{ id: 'p1', from: '08:00', to: '16:00' }]}
        projectRows={[]}
        allocations={new Map<string, number>()}
        dailyNorm={7.4}
        monthWorkTime={[]}
        boundaryWorkTime={[]}
        onPeriodsChange={vi.fn()}
        onAllocationChange={vi.fn()}
        onClose={vi.fn()}
        onOpenManager={vi.fn()}
      />,
    )
    expect(screen.queryByText(/Manuelt registreret/)).toBeNull()
    const total = screen.getByText('Arbejdstid i alt').parentElement as HTMLElement
    expect(total.textContent).toContain('8 t')
  })
})
