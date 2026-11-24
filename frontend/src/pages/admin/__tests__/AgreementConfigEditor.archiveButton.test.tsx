// AC-C2k (the frontend half; NR-15). Kind F (the ACTIVE arm) and R (the DRAFT arm).
// Red conditions: before the change an ACTIVE config renders a second "Arkiver" button in the header,
// so queryByRole('button', { name: 'Arkiver' }) is not null and the ACTIVE assertion fails. The
// server answers an ACTIVE archive with 409 ("publish a successor"), so the screen must not offer it.
// The DRAFT arm is R: a DRAFT keeps its "Arkiver" (and its dialog).
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { screen } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import { AgreementConfigEditor } from '../AgreementConfigEditor'
import { renderWithCalendar } from '../../../test/renderWithCalendar'
import { CONFIG_ID, draftConfig } from './agreementConfigFixture'

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

function renderWith(config: object) {
  mockFetch.mockReset()
  mockFetch.mockResolvedValue({
    ok: true,
    status: 200,
    headers: new Headers({ ETag: '"3"' }),
    json: async () => config,
  })
  renderWithCalendar(
    '2026-11-12',
    <MemoryRouter initialEntries={[`/global/overenskomster/${CONFIG_ID}`]}>
      <Routes>
        <Route path="/global/overenskomster/:configId" element={<AgreementConfigEditor />} />
      </Routes>
    </MemoryRouter>,
  )
}

beforeEach(() => mockFetch.mockReset())

describe('AgreementConfigEditor — "Arkiver" is offered for a DRAFT only', () => {
  it('an ACTIVE config renders no "Arkiver" button (it offers a clone instead)', async () => {
    renderWith({ ...draftConfig, status: 'ACTIVE', publishedAt: '2026-11-02T08:00:00Z', appliesFrom: '2026-11-02' })
    await screen.findByText('AC / OK24')
    expect(screen.getByRole('button', { name: 'Klon som kladde' })).toBeDefined()
    expect(screen.queryByRole('button', { name: 'Arkiver' })).toBeNull()
  })

  it('a DRAFT renders its "Arkiver" button (R)', async () => {
    renderWith(draftConfig)
    await screen.findByText('AC / OK24')
    expect(screen.getByRole('button', { name: 'Arkiver' })).toBeDefined()
  })
})
