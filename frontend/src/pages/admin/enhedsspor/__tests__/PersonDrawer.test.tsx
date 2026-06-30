// SPRINT-109 / TASK-10901 — the Person drawer renders the design §3 fields and the
// Placering options RELOAD when the Organisation changes. Rendered in CREATE mode:
// the reused LifecycleSections fires NO reporting-lines GETs in create mode (its
// resolve effect early-returns), so the drawer renders fully offline. A real
// ToastProvider satisfies the reused cores' useToast; useAuth is a LocalHR mock.

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { ToastProvider } from '../../../../components/ui/Toast'
import type { ForestMaoNode } from '../../../../hooks/useForest'
import { orgsFromForest } from '../personDrawerData'

const auth = vi.hoisted(() => ({ role: 'LocalHR' as string | null }))
vi.mock('../../../../contexts/AuthContext', () => ({
  useAuth: () => ({ role: auth.role }),
}))

import { PersonDrawer } from '../PersonDrawer'

const VEJL = '000000d0-0000-0000-0000-0000000000a1'
const KONTROL = '000000d0-0000-0000-0000-0000000000a2'
const INDKOEB = '000000d0-0000-0000-0000-0000000000b1'

function makeForest(): ForestMaoNode[] {
  return [
    {
      orgId: 'MIN01',
      orgName: 'Finansministeriet',
      orgType: 'MAO',
      parentOrgId: null,
      materializedPath: '/MIN01/',
      memberCount: 0,
      organisations: [
        {
          orgId: 'STY02', orgName: 'Statens IT', orgType: 'ORGANISATION', parentOrgId: 'MIN01',
          materializedPath: '/MIN01/STY02/', agreementCode: 'HK', okVersion: 'OK24',
          memberCount: 0, directMemberCount: 0,
          units: [
            {
              unitId: VEJL, organisationId: 'STY02', parentUnitId: null, type: 'kontor', name: 'Vejledning',
              level: 1, version: 1, directMemberCount: 0, memberCount: 0,
              children: [
                { unitId: KONTROL, organisationId: 'STY02', parentUnitId: VEJL, type: 'team', name: 'Kontrol', level: 2, version: 1, directMemberCount: 0, memberCount: 0, children: [] },
              ],
            },
          ],
        },
        {
          orgId: 'STY03', orgName: 'Statens Indkøb', orgType: 'ORGANISATION', parentOrgId: 'MIN01',
          materializedPath: '/MIN01/STY03/', agreementCode: 'AC', okVersion: 'OK24',
          memberCount: 0, directMemberCount: 0,
          units: [
            { unitId: INDKOEB, organisationId: 'STY03', parentUnitId: null, type: 'kontor', name: 'Indkøb', level: 1, version: 1, directMemberCount: 0, memberCount: 0, children: [] },
          ],
        },
      ],
    },
  ]
}

const optionTexts = (testid: string): string[] =>
  Array.from((screen.getByTestId(testid) as HTMLSelectElement).options).map((o) => (o.textContent ?? '').trim())

function renderCreate(defaultUnitId: string | null = VEJL) {
  const forest = makeForest()
  return render(
    <ToastProvider>
      <PersonDrawer
        open
        organizations={orgsFromForest(forest)}
        forest={forest}
        defaultOrgId="STY02"
        defaultUnitId={defaultUnitId}
        onClose={vi.fn()}
        onSaved={vi.fn()}
      />
    </ToastProvider>,
  )
}

beforeEach(() => {
  auth.role = 'LocalHR'
})

describe('PersonDrawer — the design §3 fields + the Placering reload', () => {
  it('renders the create-mode fields (credentials, Navn/E-mail/Organisation, Placering, apex, promote, Nærmeste leder)', () => {
    renderCreate()
    expect(screen.getByTestId('person-drawer-title').textContent).toBe('Opret medarbejder')
    // credentials (create-only)
    expect(screen.getByTestId('pd-create-user-id')).toBeDefined()
    expect(screen.getByTestId('pd-create-username')).toBeDefined()
    expect(screen.getByTestId('pd-create-password')).toBeDefined()
    // stamdata (reused)
    expect(screen.getByTestId('ep-display-name')).toBeDefined()
    expect(screen.getByTestId('ep-email')).toBeDefined()
    expect(screen.getByTestId('ep-primary-org')).toBeDefined()
    // S109 unit fields
    expect(screen.getByTestId('pd-placement')).toBeDefined()
    expect(screen.getByTestId('pd-apex')).toBeDefined()
    expect(screen.getByTestId('pd-promote')).toBeDefined()
    // the reused Nærmeste-leder (ApproverSection) renders its "Godkendes af" row.
    expect(screen.getByText('Godkendes af')).toBeDefined()
  })

  it('derives the Placering options from the forest for the chosen Organisation (incl. null = org-home), nested', () => {
    renderCreate()
    expect(optionTexts('pd-placement')).toEqual(['Direkte under organisationen', 'Vejledning', 'Kontrol'])
    // pre-selected to the unit the "+ Medarbejder" was opened on.
    expect((screen.getByTestId('pd-placement') as HTMLSelectElement).value).toBe(VEJL)
    // promote is enabled + labels the chosen unit.
    expect((screen.getByTestId('pd-promote') as HTMLInputElement).disabled).toBe(false)
    expect(screen.getByText(/Er leder af\s+Vejledning/)).toBeDefined()
  })

  it('RELOADS the Placering options when the Organisation changes (and resets to org-home + disables promote)', () => {
    renderCreate()
    fireEvent.change(screen.getByTestId('ep-primary-org'), { target: { value: 'STY03' } })
    // the STY02 units are gone; STY03's unit appears.
    expect(optionTexts('pd-placement')).toEqual(['Direkte under organisationen', 'Indkøb'])
    // the selection reset to org-home (a unit in the old org is no longer valid).
    expect((screen.getByTestId('pd-placement') as HTMLSelectElement).value).toBe('')
    // with no unit chosen, promote is disabled.
    expect((screen.getByTestId('pd-promote') as HTMLInputElement).disabled).toBe(true)
  })

  it('homes directly under the Organisation when defaultUnitId is null (promote disabled)', () => {
    renderCreate(null)
    expect((screen.getByTestId('pd-placement') as HTMLSelectElement).value).toBe('')
    expect((screen.getByTestId('pd-promote') as HTMLInputElement).disabled).toBe(true)
  })
})
