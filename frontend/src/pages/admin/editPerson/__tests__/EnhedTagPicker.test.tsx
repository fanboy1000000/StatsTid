// S97 / TASK-9705 — tests for the multi-select Enhed tag picker. Mocks fetch
// (the picker lists the person's Organisation enheder via GET /api/admin/enheder).
// Asserts: it lists the org's active enheder as checkboxes; toggling reports the
// id set up; it NAME-seeds the initial selection from currentTagNames (once) +
// fires onSeed; a 400 (MAO) shows only the legacy fallback; toggling on/off.
import { useState } from 'react'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, fireEvent } from '@testing-library/react'
import { EnhedTagPicker } from '../EnhedTagPicker'

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)
const mockStorage: Record<string, string> = { statstid_token: 'test-token' }
vi.stubGlobal('localStorage', {
  getItem: (k: string) => mockStorage[k] ?? null,
  setItem: (k: string, v: string) => { mockStorage[k] = v },
  removeItem: (k: string) => { delete mockStorage[k] },
})

function ok(json: unknown) {
  return { ok: true, status: 200, headers: new Headers(), json: async () => json, text: async () => JSON.stringify(json) }
}
function err(status: number) {
  return { ok: false, status, headers: new Headers(), json: async () => ({}), text: async () => 'x' }
}

const ENHEDER = [
  { enhedId: 'E1', organisationId: 'STY1', name: 'Netværk', version: 1 },
  { enhedId: 'E2', organisationId: 'STY1', name: 'Drift', version: 1 },
  { enhedId: 'E3', organisationId: 'STY1', name: 'Support', version: 1 },
]

/** A controlled host so toggling round-trips through real state. */
function Host(props: {
  currentTagNames?: string | null
  legacyLabel?: string | null
  onSeed?: (ids: string[]) => void
  initial?: string[]
}) {
  const [ids, setIds] = useState<string[]>(props.initial ?? [])
  return (
    <>
      <EnhedTagPicker
        organisationId="STY1"
        selectedIds={ids}
        onChange={setIds}
        currentTagNames={props.currentTagNames}
        onSeed={props.onSeed}
        legacyLabel={props.legacyLabel}
      />
      <output data-testid="selected">{ids.join(',')}</output>
    </>
  )
}

beforeEach(() => {
  mockFetch.mockReset()
})

describe('EnhedTagPicker', () => {
  it('lists the org active enheder as checkboxes', async () => {
    mockFetch.mockResolvedValue(ok(ENHEDER))
    render(<Host />)
    await waitFor(() => {
      expect(screen.getByTestId('ep-enheder-picker')).toBeDefined()
    })
    expect(screen.getByLabelText('Netværk')).toBeDefined()
    expect(screen.getByLabelText('Drift')).toBeDefined()
    expect(screen.getByLabelText('Support')).toBeDefined()
    // org id query-encoded.
    expect(mockFetch.mock.calls[0][0]).toBe('/api/admin/enheder?organisationId=STY1')
  })

  it('toggling a tag reports the selected id set up', async () => {
    mockFetch.mockResolvedValue(ok(ENHEDER))
    render(<Host />)
    await waitFor(() => expect(screen.getByLabelText('Drift')).toBeDefined())
    fireEvent.click(screen.getByLabelText('Drift'))
    expect(screen.getByTestId('selected').textContent).toBe('E2')
    fireEvent.click(screen.getByLabelText('Netværk'))
    expect(screen.getByTestId('selected').textContent).toBe('E2,E1')
    // toggling Drift OFF removes it.
    fireEvent.click(screen.getByLabelText('Drift'))
    expect(screen.getByTestId('selected').textContent).toBe('E1')
  })

  it('NAME-seeds the initial selection from currentTagNames (case-insensitive) + fires onSeed', async () => {
    mockFetch.mockResolvedValue(ok(ENHEDER))
    const onSeed = vi.fn()
    render(<Host currentTagNames="netværk, Support" onSeed={onSeed} />)
    await waitFor(() => {
      // E1 (Netværk) + E3 (Support) seeded — E2 (Drift) not.
      expect(screen.getByTestId('selected').textContent).toBe('E1,E3')
    })
    expect(onSeed).toHaveBeenCalledWith(['E1', 'E3'])
    expect((screen.getByLabelText('Netværk') as HTMLInputElement).checked).toBe(true)
    expect((screen.getByLabelText('Support') as HTMLInputElement).checked).toBe(true)
    expect((screen.getByLabelText('Drift') as HTMLInputElement).checked).toBe(false)
  })

  it('seeds EMPTY (and fires onSeed with []) when the legacy label matches no enhed name', async () => {
    mockFetch.mockResolvedValue(ok(ENHEDER))
    const onSeed = vi.fn()
    // An org-name fallback label (no structured tag) must NOT match any enhed.
    render(<Host currentTagNames="Moderniseringsstyrelsen" onSeed={onSeed} />)
    await waitFor(() => expect(screen.getByTestId('ep-enheder-picker')).toBeDefined())
    expect(screen.getByTestId('selected').textContent).toBe('')
    expect(onSeed).toHaveBeenCalledWith([])
  })

  it('a 400 (MAO org → no enheder) shows the legacy fallback, no error noise', async () => {
    mockFetch.mockResolvedValue(err(400))
    render(<Host legacyLabel="Gammel etiket" />)
    await waitFor(() => {
      expect(screen.getByTestId('ep-enheder-empty')).toBeDefined()
    })
    expect(screen.getByTestId('ep-enheder-empty').textContent).toContain('Gammel etiket')
    expect(screen.queryByTestId('ep-enheder-error')).toBeNull()
  })

  it('a 403 (out-of-scope) surfaces the error region', async () => {
    mockFetch.mockResolvedValue(err(403))
    render(<Host />)
    await waitFor(() => {
      expect(screen.getByTestId('ep-enheder-error')).toBeDefined()
    })
  })
})
