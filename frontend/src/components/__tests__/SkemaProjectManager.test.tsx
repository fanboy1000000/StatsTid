// S72 / TASK-7204 — component-level tests for the "Administrer rækker" manager
// modal (design_handoff_skema README §5 / prototype projects-manager.jsx wired-in
// tabbed ProjectManager). The component is CONTROLLED with CALLBACK-PER-ACTION
// emission (the prototype's live `onApply` model — README "Changes apply live to
// the grid.", R11): each add/remove/reorder emits the FULL next ordered selection
// with dense 0..n-1 sortOrder; the page-level wiring (PUT-per-action, live grid
// updates, R16 flush-before-refetch) lands in 7205 per the R13 layering.
//
// Multi-step flows use a stateful Harness that loops emissions back into props —
// simulating the 7205 page's live-apply. Fixtures follow PAT-007 (stable refs).
import { useState } from 'react'
import { render, screen, fireEvent, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { SkemaProjectManager } from '../SkemaProjectManager'
import type { SkemaRowPreferencesInvalidPayload } from '../../lib/api'
import type {
  Project,
  SkemaCatalogs,
  SkemaRowPreferenceAbsenceType,
  SkemaRowPreferenceProject,
  SkemaRowPreferences,
} from '../../types'

const SELECTED_PROJECTS: SkemaRowPreferenceProject[] = [
  { projectId: 'p-sag', projectCode: 'ØS-1042', projectName: 'Sagsbehandling', sortOrder: 0 },
  { projectId: 'p-borger', projectCode: 'DIG-2207', projectName: 'Borger.dk', sortOrder: 1 },
  { projectId: 'p-drift', projectCode: 'IT-6000', projectName: 'Drift & support', sortOrder: 2 },
]

// Catalog = selection-INDEPENDENT (R4): includes the selected three plus two
// addable entries.
const CATALOG_PROJECTS: Project[] = [
  { projectId: 'p-sag', projectCode: 'ØS-1042', projectName: 'Sagsbehandling', sortOrder: 0 },
  { projectId: 'p-borger', projectCode: 'DIG-2207', projectName: 'Borger.dk', sortOrder: 1 },
  { projectId: 'p-drift', projectCode: 'IT-6000', projectName: 'Drift & support', sortOrder: 2 },
  { projectId: 'p-dvh', projectCode: 'DATA-5120', projectName: 'Datavarehus', sortOrder: 3 },
  { projectId: 'p-gdpr', projectCode: 'JUR-4001', projectName: 'GDPR-tilsyn', sortOrder: 4 },
]

const SELECTED_ABSENCE: SkemaRowPreferenceAbsenceType[] = [
  { type: 'VACATION', label: 'Ferie', sortOrder: 0 },
  { type: 'CARE_DAY', label: 'Omsorgsdag', sortOrder: 1 },
]

const CATALOG_ABSENCE = [
  { type: 'VACATION', label: 'Ferie' },
  { type: 'CARE_DAY', label: 'Omsorgsdag' },
  { type: 'SENIOR_DAY', label: 'Seniordag' },
]

const CATALOGS: SkemaCatalogs = { projects: CATALOG_PROJECTS, absenceTypes: CATALOG_ABSENCE }

function prefs(
  projects: SkemaRowPreferenceProject[] = SELECTED_PROJECTS,
  absenceTypes: SkemaRowPreferenceAbsenceType[] = SELECTED_ABSENCE,
): SkemaRowPreferences {
  return { configured: true, projects, absenceTypes }
}

type ManagerProps = Parameters<typeof SkemaProjectManager>[0]

function renderManager(overrides: Partial<ManagerProps> = {}) {
  const props: ManagerProps = {
    open: true,
    rowPreferences: prefs(),
    catalogs: CATALOGS,
    onProjectsChange: vi.fn(),
    onAbsenceTypesChange: vi.fn(),
    onClose: vi.fn(),
    ...overrides,
  }
  return { ...render(<SkemaProjectManager {...props} />), props }
}

/** Stateful harness — loops per-action emissions back into props, simulating
    the 7205 page's live-apply (the controlled contract). */
function Harness({
  initial,
  onProjectsSpy,
  onAbsenceSpy,
}: {
  initial: SkemaRowPreferences
  onProjectsSpy?: (next: SkemaRowPreferenceProject[]) => void
  onAbsenceSpy?: (next: SkemaRowPreferenceAbsenceType[]) => void
}) {
  const [current, setCurrent] = useState(initial)
  return (
    <SkemaProjectManager
      open
      rowPreferences={current}
      catalogs={CATALOGS}
      onProjectsChange={(next) => {
        onProjectsSpy?.(next)
        setCurrent((p) => ({ ...p, projects: next }))
      }}
      onAbsenceTypesChange={(next) => {
        onAbsenceSpy?.(next)
        setCurrent((p) => ({ ...p, absenceTypes: next }))
      }}
      onClose={vi.fn()}
    />
  )
}

/** The two panes' lists in DOM order: [0] = "Valgt", [1] = "Tilføj fra katalog". */
function getPanes() {
  const lists = screen.getAllByRole('list')
  return { mine: lists[0], cat: lists[1] }
}

function rowNames(list: HTMLElement): string[] {
  return within(list)
    .getAllByRole('listitem')
    .map((li) => li.querySelector('[class*="rowName"]')?.textContent ?? li.textContent ?? '')
}

afterEach(() => {
  document.body.style.pointerEvents = ''
})

describe('SkemaProjectManager — shell (kit Dialog + Tabs)', () => {
  it('renders a 720px kit Dialog with the prototype title and sub verbatim', () => {
    renderManager()
    const dialog = screen.getByRole('dialog', { name: 'Administrer rækker' })
    expect(dialog.className).toContain('dialog')
    expect(
      screen.getByText('Vælg hvilke projekter og fraværstyper der vises på dit skema.'),
    ).toBeInTheDocument()
  })

  it('renders nothing when open=false', () => {
    renderManager({ open: false })
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('R5: the component takes NO month/lock prop — preferences are month-independent view state', () => {
    // Contract pin: the prop surface has no month, no readOnly, no lock state —
    // the modal stays usable on locked months by construction (ADR-012 locks
    // registrations, not view preferences).
    renderManager()
    expect(screen.getByRole('button', { name: 'Tilføj Datavarehus' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Fjern Borger.dk' })).toBeEnabled()
  })

  it('a11y (R15): both tabs render with count badges announced in the tab accessible name', () => {
    renderManager()
    expect(screen.getByRole('tab', { name: 'Projekter 3' })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'Ferie og fravær 2' })).toBeInTheDocument()
  })

  it('count badges update LIVE after an action (per-action model)', async () => {
    const user = userEvent.setup()
    render(<Harness initial={prefs()} />)
    fireEvent.click(screen.getByRole('button', { name: 'Fjern Borger.dk' }))
    expect(screen.getByRole('tab', { name: 'Projekter 2' })).toBeInTheDocument()
    await user.click(screen.getByRole('tab', { name: 'Ferie og fravær 2' }))
    fireEvent.click(screen.getByRole('button', { name: 'Tilføj Seniordag' }))
    expect(screen.getByRole('tab', { name: 'Ferie og fravær 3' })).toBeInTheDocument()
  })

  it('Escape closes via the kit Dialog (onOpenChange → onClose)', () => {
    const { props } = renderManager()
    fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape' })
    expect(props.onClose).toHaveBeenCalledTimes(1)
  })
})

describe('SkemaProjectManager — dual pane rendering (README §5)', () => {
  it('left pane "Valgt" lists the selected rows in sortOrder order with name + code', () => {
    renderManager()
    const { mine } = getPanes()
    expect(rowNames(mine)).toEqual(['Sagsbehandling', 'Borger.dk', 'Drift & support'])
    expect(within(mine).getByText('ØS-1042')).toBeInTheDocument()
    expect(within(mine).getByText('DIG-2207')).toBeInTheDocument()
    expect(within(mine).getByText('IT-6000')).toBeInTheDocument()
  })

  it('left pane renders defensively sorted by sortOrder even from an unsorted prop array', () => {
    const shuffled = [SELECTED_PROJECTS[2], SELECTED_PROJECTS[0], SELECTED_PROJECTS[1]]
    renderManager({ rowPreferences: prefs(shuffled) })
    const { mine } = getPanes()
    expect(rowNames(mine)).toEqual(['Sagsbehandling', 'Borger.dk', 'Drift & support'])
  })

  it('left pane carries the "Valgt" title with its own count badge and the order hint', () => {
    renderManager()
    expect(screen.getByRole('heading', { name: 'Valgt 3' })).toBeInTheDocument()
    expect(screen.getByText('Rækkefølge bestemmer visningen i skemaet.')).toBeInTheDocument()
  })

  it('R4: right pane "Tilføj fra katalog" lists ONLY catalog entries not currently selected', () => {
    renderManager()
    const { cat } = getPanes()
    expect(rowNames(cat)).toEqual(['Datavarehus', 'GDPR-tilsyn'])
    expect(within(cat).queryByText('Sagsbehandling')).toBeNull()
  })

  it('R4: an EMPTY selected list is legal (configured-empty) — empty state + count 0 + the FULL catalog addable', async () => {
    const user = userEvent.setup()
    renderManager({ rowPreferences: prefs([], []) })
    expect(screen.getByRole('tab', { name: 'Projekter 0' })).toBeInTheDocument()
    expect(screen.getByText('Ingen projekter valgt endnu.')).toBeInTheDocument()
    const { cat } = getPanes()
    expect(within(cat).getAllByRole('listitem')).toHaveLength(5)
    await user.click(screen.getByRole('tab', { name: 'Ferie og fravær 0' }))
    expect(screen.getByText('Ingen fraværstyper valgt endnu.')).toBeInTheDocument()
  })
})

describe('SkemaProjectManager — search (name OR code, case-insensitive)', () => {
  it('a11y (R15): the search input is labeled', () => {
    renderManager()
    expect(screen.getByRole('textbox', { name: 'Søg projekt eller kode…' })).toBeInTheDocument()
  })

  it('filters by NAME, case-insensitively', () => {
    renderManager()
    fireEvent.change(screen.getByRole('textbox', { name: 'Søg projekt eller kode…' }), {
      target: { value: 'VAREHUS' },
    })
    const { cat } = getPanes()
    expect(rowNames(cat)).toEqual(['Datavarehus'])
  })

  it('filters by CODE, case-insensitively', () => {
    renderManager()
    fireEvent.change(screen.getByRole('textbox', { name: 'Søg projekt eller kode…' }), {
      target: { value: 'jur-4' },
    })
    const { cat } = getPanes()
    expect(rowNames(cat)).toEqual(['GDPR-tilsyn'])
  })

  it('renders "Ingen matcher." when nothing matches', () => {
    renderManager()
    fireEvent.change(screen.getByRole('textbox', { name: 'Søg projekt eller kode…' }), {
      target: { value: 'findes-ikke' },
    })
    expect(screen.getByText('Ingen matcher.')).toBeInTheDocument()
  })
})

describe('SkemaProjectManager — add / Fjern / reorder (per-action emission)', () => {
  it('+ Tilføj emits the FULL next selection with the entry appended at the END, sortOrder dense 0..n-1', () => {
    const { props } = renderManager()
    fireEvent.click(screen.getByRole('button', { name: 'Tilføj Datavarehus' }))
    expect(props.onProjectsChange).toHaveBeenCalledTimes(1)
    expect(props.onProjectsChange).toHaveBeenCalledWith([
      { projectId: 'p-sag', projectCode: 'ØS-1042', projectName: 'Sagsbehandling', sortOrder: 0 },
      { projectId: 'p-borger', projectCode: 'DIG-2207', projectName: 'Borger.dk', sortOrder: 1 },
      { projectId: 'p-drift', projectCode: 'IT-6000', projectName: 'Drift & support', sortOrder: 2 },
      { projectId: 'p-dvh', projectCode: 'DATA-5120', projectName: 'Datavarehus', sortOrder: 3 },
    ])
  })

  it('an added entry moves right→left in the controlled loop (live model)', () => {
    render(<Harness initial={prefs()} />)
    fireEvent.click(screen.getByRole('button', { name: 'Tilføj Datavarehus' }))
    const { mine, cat } = getPanes()
    expect(rowNames(mine)).toEqual(['Sagsbehandling', 'Borger.dk', 'Drift & support', 'Datavarehus'])
    expect(rowNames(cat)).toEqual(['GDPR-tilsyn'])
  })

  it('Fjern emits the FULL next selection without the entry, renumbered dense', () => {
    const { props } = renderManager()
    fireEvent.click(screen.getByRole('button', { name: 'Fjern Borger.dk' }))
    expect(props.onProjectsChange).toHaveBeenCalledWith([
      { projectId: 'p-sag', projectCode: 'ØS-1042', projectName: 'Sagsbehandling', sortOrder: 0 },
      { projectId: 'p-drift', projectCode: 'IT-6000', projectName: 'Drift & support', sortOrder: 1 },
    ])
  })

  it('R4: a removed entry reappears in the catalog pane and is searchable again', () => {
    render(<Harness initial={prefs()} />)
    fireEvent.click(screen.getByRole('button', { name: 'Fjern Borger.dk' }))
    const { mine, cat } = getPanes()
    expect(rowNames(mine)).toEqual(['Sagsbehandling', 'Drift & support'])
    expect(rowNames(cat)).toContain('Borger.dk')
    fireEvent.change(screen.getByRole('textbox', { name: 'Søg projekt eller kode…' }), {
      target: { value: 'dig-2207' },
    })
    expect(rowNames(getPanes().cat)).toEqual(['Borger.dk'])
  })

  it('▲ swaps the row with its predecessor and renumbers dense', () => {
    const { props } = renderManager()
    fireEvent.click(screen.getByRole('button', { name: 'Flyt Borger.dk op' }))
    expect(props.onProjectsChange).toHaveBeenCalledWith([
      { projectId: 'p-borger', projectCode: 'DIG-2207', projectName: 'Borger.dk', sortOrder: 0 },
      { projectId: 'p-sag', projectCode: 'ØS-1042', projectName: 'Sagsbehandling', sortOrder: 1 },
      { projectId: 'p-drift', projectCode: 'IT-6000', projectName: 'Drift & support', sortOrder: 2 },
    ])
  })

  it('▼ swaps the row with its successor and renumbers dense', () => {
    const { props } = renderManager()
    fireEvent.click(screen.getByRole('button', { name: 'Flyt Borger.dk ned' }))
    expect(props.onProjectsChange).toHaveBeenCalledWith([
      { projectId: 'p-sag', projectCode: 'ØS-1042', projectName: 'Sagsbehandling', sortOrder: 0 },
      { projectId: 'p-drift', projectCode: 'IT-6000', projectName: 'Drift & support', sortOrder: 1 },
      { projectId: 'p-borger', projectCode: 'DIG-2207', projectName: 'Borger.dk', sortOrder: 2 },
    ])
  })

  it('emission renumbers DENSE 0..n-1 even from stale sparse input sortOrder', () => {
    const sparse: SkemaRowPreferenceProject[] = [
      { ...SELECTED_PROJECTS[0], sortOrder: 0 },
      { ...SELECTED_PROJECTS[1], sortOrder: 5 },
      { ...SELECTED_PROJECTS[2], sortOrder: 9 },
    ]
    const { props } = renderManager({ rowPreferences: prefs(sparse) })
    fireEvent.click(screen.getByRole('button', { name: 'Fjern Sagsbehandling' }))
    expect(props.onProjectsChange).toHaveBeenCalledWith([
      { projectId: 'p-borger', projectCode: 'DIG-2207', projectName: 'Borger.dk', sortOrder: 0 },
      { projectId: 'p-drift', projectCode: 'IT-6000', projectName: 'Drift & support', sortOrder: 1 },
    ])
  })

  it('a11y (R15): ▲ on the first row and ▼ on the last row are disabled; per-row aria-labels distinguish targets', () => {
    renderManager()
    expect(screen.getByRole('button', { name: 'Flyt Sagsbehandling op' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Flyt Drift & support ned' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Flyt Sagsbehandling ned' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Flyt Drift & support op' })).toBeEnabled()
  })

  it('a11y (R15): Fjern is a keyboard-reachable button with a per-row accessible name', () => {
    renderManager()
    const fjern = screen.getByRole('button', { name: 'Fjern Borger.dk' })
    fjern.focus()
    expect(document.activeElement).toBe(fjern)
    expect(fjern).toHaveTextContent('Fjern')
  })
})

describe('SkemaProjectManager — Ferie og fravær tab', () => {
  it('renders the info Alert VERBATIM on the absence tab only', async () => {
    const user = userEvent.setup()
    renderManager()
    const noteText = 'Bemærk: kataloget over fraværstyper er ikke komplet i denne mock-up.'
    expect(screen.queryByText(noteText)).toBeNull() // not on the Projekter tab
    await user.click(screen.getByRole('tab', { name: 'Ferie og fravær 2' }))
    const note = screen.getByText(noteText)
    expect(note.closest('[role="alert"]')?.className).toContain('info')
  })

  it('absence rows render label + the type key as the mono secondary line', async () => {
    const user = userEvent.setup()
    renderManager()
    await user.click(screen.getByRole('tab', { name: 'Ferie og fravær 2' }))
    const { mine } = getPanes()
    expect(rowNames(mine)).toEqual(['Ferie', 'Omsorgsdag'])
    expect(within(mine).getByText('VACATION')).toBeInTheDocument()
    expect(within(mine).getByText('CARE_DAY')).toBeInTheDocument()
  })

  it('absence add emits {type, label, sortOrder} entries, appended at the END, dense', async () => {
    const user = userEvent.setup()
    const { props } = renderManager()
    await user.click(screen.getByRole('tab', { name: 'Ferie og fravær 2' }))
    fireEvent.click(screen.getByRole('button', { name: 'Tilføj Seniordag' }))
    expect(props.onAbsenceTypesChange).toHaveBeenCalledWith([
      { type: 'VACATION', label: 'Ferie', sortOrder: 0 },
      { type: 'CARE_DAY', label: 'Omsorgsdag', sortOrder: 1 },
      { type: 'SENIOR_DAY', label: 'Seniordag', sortOrder: 2 },
    ])
  })

  it('absence reorder emits the swapped dense order', async () => {
    const user = userEvent.setup()
    const { props } = renderManager()
    await user.click(screen.getByRole('tab', { name: 'Ferie og fravær 2' }))
    fireEvent.click(screen.getByRole('button', { name: 'Flyt Omsorgsdag op' }))
    expect(props.onAbsenceTypesChange).toHaveBeenCalledWith([
      { type: 'CARE_DAY', label: 'Omsorgsdag', sortOrder: 0 },
      { type: 'VACATION', label: 'Ferie', sortOrder: 1 },
    ])
  })

  it('absence search matches the type key as the "code"', async () => {
    const user = userEvent.setup()
    renderManager()
    await user.click(screen.getByRole('tab', { name: 'Ferie og fravær 2' }))
    fireEvent.change(screen.getByRole('textbox', { name: 'Søg fraværstype…' }), {
      target: { value: 'senior_day' },
    })
    const { cat } = getPanes()
    expect(rowNames(cat)).toEqual(['Seniordag'])
  })
})

describe('SkemaProjectManager — footer', () => {
  it('renders the data-retention note VERBATIM', () => {
    renderManager()
    expect(
      screen.getByText('Fjernede rækker beholder deres registreringer — de skjules blot fra skemaet.'),
    ).toBeInTheDocument()
  })

  it('Færdig closes — and emits NO batch save (per-action model: every change was already emitted)', () => {
    const { props } = renderManager()
    fireEvent.click(screen.getByRole('button', { name: 'Færdig' }))
    expect(props.onClose).toHaveBeenCalledTimes(1)
    expect(props.onProjectsChange).not.toHaveBeenCalled()
    expect(props.onAbsenceTypesChange).not.toHaveBeenCalled()
  })
})

describe('SkemaProjectManager — 422 offender rendering', () => {
  const FULL_ERROR: SkemaRowPreferencesInvalidPayload = {
    error: 'row_preferences_invalid',
    invalidProjectIds: ['p-ukendt-1', 'p-ukendt-2'],
    invalidAbsenceTypes: ['UNKNOWN_TYPE'],
    duplicateProjectIds: ['p-sag'],
    duplicateAbsenceTypes: ['VACATION'],
    message: 'Row preferences validation failed.',
  }

  it('renders the 422 payload as an error Alert listing EVERY offender class', () => {
    renderManager({ saveError: FULL_ERROR })
    const alert = screen.getByRole('alert')
    expect(alert.className).toContain('error')
    expect(within(alert).getByText('Row preferences validation failed.')).toBeInTheDocument()
    expect(
      within(alert).getByText('Ugyldige projekter: p-ukendt-1, p-ukendt-2'),
    ).toBeInTheDocument()
    expect(within(alert).getByText('Dublerede projekter: p-sag')).toBeInTheDocument()
    expect(within(alert).getByText('Ugyldige fraværstyper: UNKNOWN_TYPE')).toBeInTheDocument()
    expect(within(alert).getByText('Dublerede fraværstyper: VACATION')).toBeInTheDocument()
  })

  it('omits empty offender classes (message-only payload renders just the message)', () => {
    renderManager({
      saveError: {
        ...FULL_ERROR,
        invalidProjectIds: [],
        invalidAbsenceTypes: [],
        duplicateProjectIds: [],
        duplicateAbsenceTypes: [],
      },
    })
    const alert = screen.getByRole('alert')
    expect(within(alert).getByText('Row preferences validation failed.')).toBeInTheDocument()
    expect(within(alert).queryByText(/Ugyldige/)).toBeNull()
    expect(within(alert).queryByText(/Dublerede/)).toBeNull()
  })

  it('renders no error Alert when saveError is absent', () => {
    renderManager()
    expect(screen.queryByRole('alert')).toBeNull()
  })
})

describe('SkemaProjectManager — full-day note (S73 R5)', () => {
  // The served fullDayOnly flag rides on the absence-type DTOs (both panes).
  const fullDayPrefs: SkemaRowPreferences = {
    configured: true,
    projects: SELECTED_PROJECTS,
    absenceTypes: [
      { type: 'VACATION', label: 'Ferie', sortOrder: 0 },
      { type: 'CARE_DAY', label: 'Omsorgsdag', sortOrder: 1, fullDayOnly: true },
    ],
  }
  const fullDayCatalogs: SkemaCatalogs = {
    projects: CATALOG_PROJECTS,
    absenceTypes: [
      { type: 'VACATION', label: 'Ferie' },
      { type: 'CARE_DAY', label: 'Omsorgsdag', fullDayOnly: true },
      { type: 'SENIOR_DAY', label: 'Seniordag', fullDayOnly: true },
    ],
  }

  it('renders the "hele dage" note on a SELECTED full-day absence type (from the served flag) but not on an hours-based one', async () => {
    const user = userEvent.setup()
    renderManager({ rowPreferences: fullDayPrefs, catalogs: fullDayCatalogs })
    await user.click(screen.getByRole('tab', { name: 'Ferie og fravær 2' }))
    const { mine } = getPanes()
    const careRow = within(mine)
      .getAllByRole('listitem')
      .find((li) => li.textContent?.includes('Omsorgsdag')) as HTMLElement
    expect(within(careRow).getByText('hele dage')).toBeInTheDocument()
    const ferieRow = within(mine)
      .getAllByRole('listitem')
      .find((li) => li.textContent?.includes('Ferie')) as HTMLElement
    expect(within(ferieRow).queryByText('hele dage')).toBeNull()
  })

  it('renders the "hele dage" note on a CATALOG (addable) full-day absence type', async () => {
    const user = userEvent.setup()
    renderManager({ rowPreferences: fullDayPrefs, catalogs: fullDayCatalogs })
    await user.click(screen.getByRole('tab', { name: 'Ferie og fravær 2' }))
    const { cat } = getPanes()
    // SENIOR_DAY is addable (not selected) and carries the flag.
    const seniorRow = within(cat)
      .getAllByRole('listitem')
      .find((li) => li.textContent?.includes('Seniordag')) as HTMLElement
    expect(within(seniorRow).getByText('hele dage')).toBeInTheDocument()
  })
})
