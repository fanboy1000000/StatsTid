import { describe, it, expect } from 'vitest'
import type {
  ApprovalValidationError,
  AllocationValidationError,
  CoverageValidationError,
} from '../useSkema'

/**
 * S56 / TASK-5604 — the employee-approve 422 is a DISCRIMINATED UNION on `kind`
 * ('allocation' | 'coverage'). The UI must render directional Danish guidance —
 * never raw JSON. This pins both branches of SkemaPage.formatApprovalValidationError
 * (the production formatter is module-private, so this is a verbatim spec mirror;
 * see the agent report's DECLARE note recommending it be exported for direct test).
 *
 * Directional contract (worked vs allocated):
 *  - 'under' (worked > allocated): "Fordel de resterende {Δ} t på projekter for {dato}"
 *  - 'over'  (allocated > worked): "Registrér arbejdstid for de {Δ} t for {dato}"
 */

// ── verbatim mirror of SkemaPage.tsx:46-66 ──
function formatHoursDa(h: number): string {
  return h.toFixed(2).replace(/\.?0+$/, '').replace('.', ',')
}

function formatDanishDate(dateStr: string): string {
  try {
    const d = new Date(dateStr + 'T00:00:00')
    return d.toLocaleDateString('da-DK', { weekday: 'long', day: 'numeric', month: 'long' })
  } catch {
    return dateStr
  }
}

function formatApprovalValidationError(err: ApprovalValidationError): string[] {
  if (err.kind === 'allocation') {
    return err.unbalancedDays.map((d) => {
      const dato = formatDanishDate(d.date)
      if (d.direction === 'under') {
        const remaining = formatHoursDa(d.worked - d.allocated)
        return `Fordel de resterende ${remaining} t på projekter for ${dato}`
      }
      const excess = formatHoursDa(d.allocated - d.worked)
      return `Registrér arbejdstid for de ${excess} t for ${dato}`
    })
  }
  const daysList = err.missingDays.map(formatDanishDate).join(', ')
  return [
    `Ikke alle arbejdsdage er dækket (${err.coveredDays} af ${err.totalWorkdays}). Følgende dage mangler registreringer: ${daysList}`,
  ]
}

describe('formatApprovalValidationError — allocation branch', () => {
  it('renders directional "under" (worked > allocated) guidance, not raw JSON', () => {
    const err: AllocationValidationError = {
      kind: 'allocation',
      unbalancedDays: [{ date: '2026-03-05', worked: 7.4, allocated: 3.0, direction: 'under' }],
    }
    const [msg] = formatApprovalValidationError(err)
    expect(msg).toContain('Fordel de resterende')
    expect(msg).toContain('4,4 t')        // Δ = 7.4 - 3.0, Danish comma
    expect(msg).not.toContain('{')        // no raw JSON / template leakage
    expect(msg).not.toContain('"kind"')
  })

  it('renders directional "over" (allocated > worked) guidance', () => {
    const err: AllocationValidationError = {
      kind: 'allocation',
      unbalancedDays: [{ date: '2026-03-05', worked: 0, allocated: 7.4, direction: 'over' }],
    }
    const [msg] = formatApprovalValidationError(err)
    expect(msg).toContain('Registrér arbejdstid for de')
    expect(msg).toContain('7,4 t')
  })

  it('renders one message per unbalanced day', () => {
    const err: AllocationValidationError = {
      kind: 'allocation',
      unbalancedDays: [
        { date: '2026-03-05', worked: 7.4, allocated: 3.0, direction: 'under' },
        { date: '2026-03-06', worked: 0, allocated: 7.4, direction: 'over' },
      ],
    }
    expect(formatApprovalValidationError(err)).toHaveLength(2)
  })
})

describe('formatApprovalValidationError — coverage branch', () => {
  it('renders the coverage message (distinct from allocation), not raw JSON', () => {
    const err: CoverageValidationError = {
      kind: 'coverage',
      missingDays: ['2026-03-05'],
      coveredDays: 20,
      totalWorkdays: 21,
    }
    const [msg] = formatApprovalValidationError(err)
    expect(msg).toContain('Ikke alle arbejdsdage er dækket')
    expect(msg).toContain('(20 af 21)')
    expect(msg).not.toContain('Fordel de resterende') // not the allocation copy
    expect(msg).not.toContain('"missingDays"')
  })
})
