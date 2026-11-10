// AC-S0-8a (TASK-14807, S0-8 stage-0 label). Kind F.
// Red conditions: every `getByTestId('stage0-label')` below fails on code
// without the label (no element carries that test id), and the text assertions
// fail without the three clauses, on each render (agreement editor new and
// existing, position-override page, local profile editor).
// The label says: affects screens and booking; does not yet affect pay; the rest
// check still reads the built-in values. It must read identically on all pages.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
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

function expectLabel(): string {
  const text = screen.getByTestId('stage0-label').textContent ?? ''
  expect(text).toMatch(/skærmbilleder og booking/)
  expect(text).toMatch(/endnu ikke/)
  expect(text).toMatch(/hviletidskontrollen.*indbyggede værdier/i)
  return text
}

function renderAgreementEditor(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/global/overenskomster/new" element={<AgreementConfigEditor />} />
        <Route path="/global/overenskomster/:configId" element={<AgreementConfigEditor />} />
      </Routes>
    </MemoryRouter>,
  )
}

function renderProfileEditor() {
  return render(
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
}

describe('stage-0 label on the config editors', () => {
  it('is on the agreement editor for a new config', async () => {
    renderAgreementEditor('/global/overenskomster/new')
    await waitFor(() => expectLabel())
  })

  it('is on the agreement editor for an existing config', async () => {
    // The page shows editable content only once the config has loaded.
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
    await waitFor(() => expectLabel())
  })

  it('is on the position-override page', () => {
    render(<PositionOverrideManagement />)
    expectLabel()
  })

  it('is on the local profile editor', () => {
    renderProfileEditor()
    expectLabel()
  })

  it('reads identically on all three pages', async () => {
    const texts: string[] = []
    const a = render(<PositionOverrideManagement />)
    texts.push(expectLabel())
    a.unmount()
    const b = renderProfileEditor()
    texts.push(expectLabel())
    b.unmount()
    renderAgreementEditor('/global/overenskomster/new')
    await waitFor(() => texts.push(expectLabel()))
    expect(new Set(texts).size).toBe(1)
  })
})
