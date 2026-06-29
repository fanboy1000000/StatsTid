// SPRINT-107 / TASK-10704 — vitest for the AFGRÆNSNING scope control.
//
// The keystone (Step-0b): the popover OPTIONS derive from the already-scope
// -bounded forest — a scoped fixture (one MAO, one of its two orgs visible) must
// show ONLY the admitted org names, NEVER an out-of-scope one. Plus: the tri-state
// ministerområde checkbox, "Vælg alle" / "Ryd", and "Anvend" applying the chosen
// org set (normalized to null when it covers every org).
import { describe, it, expect, vi } from 'vitest'
import { useState } from 'react'
import { render, screen, fireEvent, within } from '@testing-library/react'
import { AfgraensningControl } from '../AfgraensningControl'
import type { ForestMaoNode } from '../../../../hooks/useForest'

// A scoped forest: the MAO "Finansministeriet" with only ONE of its real orgs
// admitted (STY02). "Statens Adm" (STY99) is DELIBERATELY ABSENT from the forest —
// it is out of the actor's scope, so it must never appear in the popover.
function scopedForest(): ForestMaoNode[] {
  return [
    {
      orgId: 'MIN01',
      orgName: 'Finansministeriet',
      orgType: 'MAO',
      parentOrgId: null,
      materializedPath: '/MIN01/',
      memberCount: 30,
      organisations: [
        {
          orgId: 'STY02',
          orgName: 'Statens IT',
          orgType: 'ORGANISATION',
          parentOrgId: 'MIN01',
          materializedPath: '/MIN01/STY02/',
          agreementCode: 'HK',
          okVersion: 'OK24',
          memberCount: 30,
          directMemberCount: 0,
          units: [],
        },
      ],
    },
  ]
}

// A two-org MAO forest (both orgs visible) for the tri-state + apply assertions.
function twoOrgForest(): ForestMaoNode[] {
  return [
    {
      orgId: 'MIN01',
      orgName: 'Finansministeriet',
      orgType: 'MAO',
      parentOrgId: null,
      materializedPath: '/MIN01/',
      memberCount: 50,
      organisations: [
        {
          orgId: 'STY02',
          orgName: 'Statens IT',
          orgType: 'ORGANISATION',
          parentOrgId: 'MIN01',
          materializedPath: '/MIN01/STY02/',
          agreementCode: 'HK',
          okVersion: 'OK24',
          memberCount: 30,
          directMemberCount: 0,
          units: [],
        },
        {
          orgId: 'STY03',
          orgName: 'Statens Indkøb',
          orgType: 'ORGANISATION',
          parentOrgId: 'MIN01',
          materializedPath: '/MIN01/STY03/',
          agreementCode: 'AC',
          okVersion: 'OK24',
          memberCount: 20,
          directMemberCount: 0,
          units: [],
        },
      ],
    },
  ]
}

/** A tiny controlled host so `value` reflects the applied `onChange`. */
function Host({ forest, onApply }: { forest: ForestMaoNode[]; onApply?: (v: Set<string> | null) => void }) {
  const [value, setValue] = useState<Set<string> | null>(null)
  return (
    <AfgraensningControl
      forest={forest}
      value={value}
      onChange={(next) => {
        setValue(next)
        onApply?.(next)
      }}
    />
  )
}

describe('AfgraensningControl — the scope popover', () => {
  it('summary shows "Alle organisationer" by default (null = all)', () => {
    render(<Host forest={twoOrgForest()} />)
    expect(screen.getByTestId('afgraensning-summary').textContent).toBe('Alle organisationer')
  })

  it('the OPTIONS derive from the scope-bounded forest — no out-of-scope org appears', () => {
    render(<Host forest={scopedForest()} />)
    fireEvent.click(screen.getByTestId('afgraensning-trigger'))
    const popover = screen.getByTestId('afgraensning-popover')
    // The admitted MAO + its admitted org are listed…
    expect(within(popover).getByText('Finansministeriet')).toBeDefined()
    expect(within(popover).getByText('Statens IT')).toBeDefined()
    // …but the out-of-scope org (absent from the forest) is NOT.
    expect(within(popover).queryByText('Statens Adm')).toBeNull()
    expect(screen.queryByTestId('afg-org-STY99')).toBeNull()
  })

  it('the ministerområde checkbox is tri-state (all ✓ / some – / none) over its orgs', () => {
    render(<Host forest={twoOrgForest()} />)
    fireEvent.click(screen.getByTestId('afgraensning-trigger'))
    const mao = screen.getByTestId('afg-mao-MIN01')
    // Default (all selected) → checked.
    expect(mao.getAttribute('aria-checked')).toBe('true')
    // Deselect one org → the parent becomes "mixed".
    fireEvent.click(screen.getByTestId('afg-org-STY03'))
    expect(screen.getByTestId('afg-mao-MIN01').getAttribute('aria-checked')).toBe('mixed')
    // Clicking a "mixed" parent selects ALL its orgs → checked again.
    fireEvent.click(screen.getByTestId('afg-mao-MIN01'))
    expect(screen.getByTestId('afg-mao-MIN01').getAttribute('aria-checked')).toBe('true')
    expect(screen.getByTestId('afg-org-STY03').getAttribute('aria-checked')).toBe('true')
  })

  it('"Anvend" applies a NARROWED subset; the summary recomputes to "1 organisation"', () => {
    const onApply = vi.fn()
    render(<Host forest={twoOrgForest()} onApply={onApply} />)
    fireEvent.click(screen.getByTestId('afgraensning-trigger'))
    fireEvent.click(screen.getByTestId('afg-org-STY03')) // drop one org
    fireEvent.click(screen.getByTestId('afg-apply'))
    // Applied a real Set (not null) with only the kept org.
    const applied = onApply.mock.calls[0][0] as Set<string> | null
    expect(applied).not.toBeNull()
    expect([...(applied as Set<string>)]).toEqual(['STY02'])
    // The popover closed and the summary recomputed.
    expect(screen.queryByTestId('afgraensning-popover')).toBeNull()
    expect(screen.getByTestId('afgraensning-summary').textContent).toBe('1 organisation')
  })

  it('"Ryd" then "Anvend" yields "Intet valgt"; "Vælg alle" normalizes back to null/all', () => {
    render(<Host forest={twoOrgForest()} />)
    fireEvent.click(screen.getByTestId('afgraensning-trigger'))
    fireEvent.click(screen.getByTestId('afg-clear'))
    fireEvent.click(screen.getByTestId('afg-apply'))
    expect(screen.getByTestId('afgraensning-summary').textContent).toBe('Intet valgt')
    // Reopen, select all, apply → "Alle organisationer".
    fireEvent.click(screen.getByTestId('afgraensning-trigger'))
    fireEvent.click(screen.getByTestId('afg-select-all'))
    fireEvent.click(screen.getByTestId('afg-apply'))
    expect(screen.getByTestId('afgraensning-summary').textContent).toBe('Alle organisationer')
  })

  it('only narrows: it exposes NO mutation/create affordance (the allowed buttons only)', () => {
    render(<Host forest={twoOrgForest()} />)
    fireEvent.click(screen.getByTestId('afgraensning-trigger'))
    // None of the S108 mutation labels exist anywhere in the control.
    for (const label of ['Tilføj', '+ Organisation', 'Rediger', 'Slet', 'Omdøb', 'Flyt']) {
      expect(screen.queryByText(label)).toBeNull()
    }
  })
})
