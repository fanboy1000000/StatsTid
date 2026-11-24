// AC-S0-8b (1) (TASK-14829, S0-8 / R-26). Kind F.
// Red conditions: before the change the stage-0 label renders on all three pages, so
// queryByTestId('stage0-label') is not null and the text matchers find "skærmbilleder og booking"
// and "indbyggede værdier". Rendered on the agreement editor (new and existing), the
// position-override page and the local profile editor, as the retired stage-0 test rendered them.
// The label goes in the merge that makes these values reach pay, so it is never false.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import { renderWithCalendar } from '../../../test/renderWithCalendar'
import { AgreementConfigEditor } from '../AgreementConfigEditor'
import { PositionOverrideManagement } from '../PositionOverrideManagement'
import { ProfileEditor } from '../../../components/config/ProfileEditor'

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

const mockStorage: Record<string, string> = {}
vi.stubGlobal('localStorage', {
  getItem: (key: string) => mockStorage[key] ?? null,
  setItem: (key: string, val: string) => { mockStorage[key] = val },
  removeItem: (key: string) => { delete mockStorage[key] },
})

beforeEach(() => {
  mockFetch.mockReset()
  mockFetch.mockReturnValue(new Promise(() => {}))
  Object.keys(mockStorage).forEach((k) => delete mockStorage[k])
})

function expectNoLabel() {
  expect(screen.queryByTestId('stage0-label')).toBeNull()
  expect(screen.queryByText(/skærmbilleder og booking/i)).toBeNull()
  expect(screen.queryByText(/indbyggede værdier/i)).toBeNull()
}

// S148 T23: both editors read the Copenhagen today and the override page reads the role, so they render
// inside a calendar and under a GlobalAdmin.
vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({ role: 'GlobalAdmin' }),
}))

function renderAgreementEditor(path: string) {
  return renderWithCalendar('2026-11-12',
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/global/overenskomster/new" element={<AgreementConfigEditor />} />
        <Route path="/global/overenskomster/:configId" element={<AgreementConfigEditor />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('the stage-0 label is gone from the config editors', () => {
  it('agreement editor, new config', async () => {
    renderAgreementEditor('/global/overenskomster/new')
    await screen.findByRole('button', { name: 'Tilbage' })
    expectNoLabel()
  })

  it('agreement editor, existing config', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      status: 200,
      headers: new Headers({ ETag: '"1"' }),
      json: async () => ({
        configId: '11111111-1111-1111-1111-111111111111',
        agreementCode: 'AC',
        okVersion: 'OK24',
        status: 'DRAFT',
        version: 1,
        entitlements: [],
        entitlementsReadOnly: false,
      }),
    })
    renderAgreementEditor('/global/overenskomster/11111111-1111-1111-1111-111111111111')
    await screen.findByRole('button', { name: 'Tilbage' })
    await waitFor(() => expect(mockFetch).toHaveBeenCalled())
    expectNoLabel()
  })

  it('position-override page', () => {
    renderWithCalendar('2026-11-12', <PositionOverrideManagement />)
    expectNoLabel()
  })

  it('local profile editor', () => {
    render(
      <ProfileEditor
        orgId="ORG1"
        agreementCode="AC"
        okVersion="OK24"
        orgLabel="Test org"
        profile={null}
        etag={null}
        centralConfig={null}
        loading={false}
        loadError={null}
        onSaved={() => {}}
      />,
    )
    expectNoLabel()
  })
})
