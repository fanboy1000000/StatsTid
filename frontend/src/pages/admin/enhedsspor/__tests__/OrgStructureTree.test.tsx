// SPRINT-107 / TASK-10702 — vitest for the LEFT org-structure tree.
//
// The fixture mirrors the REAL S106 forest wire shape (the S97→S99→S100
// "fetchEnheder" drift-class fix: a FE test mock must NOT diverge from the
// backend's actual JSON — see ForestEndpointContractTests). Asserts: the
// MAO→Organisation→units nesting renders; the deep member-count pills; the
// per-type accent dots; expand/collapse of a unit sub-tree; and that selection
// lifts the node (driving `selectedId` → the green-border + tint highlight).
import { describe, it, expect, vi } from 'vitest'
import { useState } from 'react'
import { render, screen, fireEvent, within } from '@testing-library/react'
import { OrgStructureTree, type SelectedNode } from '../OrgStructureTree'
import type { ForestMaoNode } from '../../../../hooks/useForest'

// IDs as the backend serializes them: orgId is a code string, unitId a GUID.
const U_DIREKTION = '000000d0-0000-0000-0000-000000000001'
const U_OMRADE = '000000d0-0000-0000-0000-000000000002'
const U_KONTOR = '000000d0-0000-0000-0000-000000000003'

/** A nested MAO → Organisation → (Direktion → Område → Kontor) fixture in the
    REAL forest shape (camelCase, the envelope-less node arrays). */
function makeForest(): ForestMaoNode[] {
  return [
    {
      orgId: 'MIN01',
      orgName: 'Finansministeriet',
      orgType: 'MAO',
      parentOrgId: null,
      materializedPath: '/MIN01/',
      memberCount: 42,
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
          directMemberCount: 4,
          units: [
            {
              unitId: U_DIREKTION,
              organisationId: 'STY02',
              parentUnitId: null,
              type: 'direktion',
              name: 'Direktion',
              level: 1,
              version: 1,
              directMemberCount: 1,
              memberCount: 26,
              children: [
                {
                  unitId: U_OMRADE,
                  organisationId: 'STY02',
                  parentUnitId: U_DIREKTION,
                  type: 'omrade',
                  name: 'Driftsomraadet',
                  level: 2,
                  version: 1,
                  directMemberCount: 0,
                  memberCount: 25,
                  children: [
                    {
                      unitId: U_KONTOR,
                      organisationId: 'STY02',
                      parentUnitId: U_OMRADE,
                      type: 'kontor',
                      name: 'IT-Drift',
                      level: 3,
                      version: 1,
                      directMemberCount: 25,
                      memberCount: 25,
                      children: [],
                    },
                  ],
                },
              ],
            },
          ],
        },
      ],
    },
  ]
}

describe('OrgStructureTree', () => {
  it('renders the MAO → Organisation → units nesting (org tiers open by default; deeper units collapsed)', () => {
    render(
      <OrgStructureTree
        forest={makeForest()}
        loading={false}
        error={null}
        selectedId={null}
        onSelect={vi.fn()}
      />,
    )
    // MAO + Organisation + the top-level unit are visible (org tiers default-open).
    expect(screen.getByText('Finansministeriet')).toBeDefined()
    expect(screen.getByText('Statens IT')).toBeDefined()
    expect(screen.getByText('Direktion')).toBeDefined()
    // The Direktion's children (deeper units) are collapsed by default.
    expect(screen.queryByText('Driftsomraadet')).toBeNull()
  })

  it('renders the deep member-count pills', () => {
    render(
      <OrgStructureTree
        forest={makeForest()}
        loading={false}
        error={null}
        selectedId={null}
        onSelect={vi.fn()}
      />,
    )
    expect(screen.getByTestId('tree-count-MIN01').textContent).toBe('42')
    expect(screen.getByTestId('tree-count-STY02').textContent).toBe('30')
    expect(screen.getByTestId(`tree-count-${U_DIREKTION}`).textContent).toBe('26')
  })

  it('colours each row dot from the per-type --unit-accent token', () => {
    render(
      <OrgStructureTree
        forest={makeForest()}
        loading={false}
        error={null}
        selectedId={null}
        onSelect={vi.fn()}
      />,
    )
    expect(screen.getByTestId('tree-dot-MIN01').style.backgroundColor).toBe('var(--unit-accent-ministeromrade)')
    expect(screen.getByTestId('tree-dot-STY02').style.backgroundColor).toBe('var(--unit-accent-organisation)')
    expect(screen.getByTestId(`tree-dot-${U_DIREKTION}`).style.backgroundColor).toBe('var(--unit-accent-direktion)')
  })

  it('expands and collapses a unit sub-tree via its caret', () => {
    render(
      <OrgStructureTree
        forest={makeForest()}
        loading={false}
        error={null}
        selectedId={null}
        onSelect={vi.fn()}
      />,
    )
    // Direktion is collapsed → its child is hidden.
    expect(screen.queryByText('Driftsomraadet')).toBeNull()
    // Expand Direktion → the child Område appears (its kontor child stays collapsed).
    fireEvent.click(screen.getByTestId(`tree-caret-${U_DIREKTION}`))
    expect(screen.getByText('Driftsomraadet')).toBeDefined()
    expect(screen.queryByText('IT-Drift')).toBeNull()
    // Collapse again → the child disappears.
    fireEvent.click(screen.getByTestId(`tree-caret-${U_DIREKTION}`))
    expect(screen.queryByText('Driftsomraadet')).toBeNull()
  })

  it('clicking the caret toggles expansion WITHOUT selecting the row', () => {
    const onSelect = vi.fn()
    render(
      <OrgStructureTree
        forest={makeForest()}
        loading={false}
        error={null}
        selectedId={null}
        onSelect={onSelect}
      />,
    )
    fireEvent.click(screen.getByTestId(`tree-caret-${U_DIREKTION}`))
    expect(onSelect).not.toHaveBeenCalled()
  })

  it('lifts the selected node on row click (id/kind/name/type)', () => {
    const onSelect = vi.fn()
    render(
      <OrgStructureTree
        forest={makeForest()}
        loading={false}
        error={null}
        selectedId={null}
        onSelect={onSelect}
      />,
    )
    fireEvent.click(screen.getByText('Direktion'))
    expect(onSelect).toHaveBeenCalledWith<[SelectedNode]>({
      id: U_DIREKTION,
      kind: 'unit',
      name: 'Direktion',
      type: 'direktion',
    })
  })

  it('selection updates selectedId → the selected row gets aria-selected (the highlight)', () => {
    // A tiny controlled wrapper mirroring the page: selecting a node drives
    // selectedId back into the tree, which marks the row selected.
    function Harness() {
      const [selected, setSelected] = useState<SelectedNode | null>(null)
      return (
        <OrgStructureTree
          forest={makeForest()}
          loading={false}
          error={null}
          selectedId={selected?.id ?? null}
          onSelect={setSelected}
        />
      )
    }
    render(<Harness />)
    const row = screen.getByTestId('tree-row-STY02')
    expect(row.getAttribute('aria-selected')).toBe('false')
    fireEvent.click(within(row).getByText('Statens IT'))
    expect(screen.getByTestId('tree-row-STY02').getAttribute('aria-selected')).toBe('true')
  })

  it('renders the loading and empty states', () => {
    const { rerender } = render(
      <OrgStructureTree forest={[]} loading={true} error={null} selectedId={null} onSelect={vi.fn()} />,
    )
    expect(screen.getByTestId('tree-loading')).toBeDefined()
    rerender(
      <OrgStructureTree forest={[]} loading={false} error={null} selectedId={null} onSelect={vi.fn()} />,
    )
    expect(screen.getByTestId('tree-empty')).toBeDefined()
  })

  it('renders the error state', () => {
    render(
      <OrgStructureTree forest={[]} loading={false} error="boom" selectedId={null} onSelect={vi.fn()} />,
    )
    expect(screen.getByTestId('tree-error')).toBeDefined()
  })
})
