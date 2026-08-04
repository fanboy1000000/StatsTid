import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { lazy, Suspense } from 'react'
import { MemoryRouter, Routes, Route, Link } from 'react-router-dom'
import { RouteErrorBoundary } from '../RouteErrorBoundary'

// S126 / W4. The regression: after F3 split the bundle, a dynamic import can REJECT (a user on a
// cached index.html after a redeploy requests a hashed chunk that no longer exists). React throws
// during render, unmounts the tree, and the page goes permanently blank. These tests use a lazy
// component whose import() actually rejects — the real failure mode — rather than a component that
// merely throws, so they exercise the Suspense+lazy path the boundary was added for.

// React logs caught errors via console.error; silence it so a passing run is not full of noise, but
// keep the spy so we can assert the boundary does NOT swallow the cause.
let consoleErr: ReturnType<typeof vi.spyOn>
beforeEach(() => { consoleErr = vi.spyOn(console, 'error').mockImplementation(() => {}) })
afterEach(() => { consoleErr.mockRestore() })

/** A lazily-imported page whose chunk fails to arrive — exactly what a stale index.html produces. */
const BrokenChunk = lazy(() =>
  Promise.reject(new Error('Failed to fetch dynamically imported module: /assets/Page-abc123.js')),
)
const GoodChunk = lazy(() => Promise.resolve({ default: () => <div>Virker fint</div> }))

function Shell({ initial = '/broken' }: { initial?: string }) {
  return (
    <MemoryRouter initialEntries={[initial]}>
      {/* The shell must survive — that is the whole point of the boundary sitting here rather than
          around the router. */}
      <header>Skal blive stående</header>
      <nav><Link to="/good">Til god side</Link></nav>
      <RouteErrorBoundary>
        <Suspense fallback={<div data-testid="fallback" />}>
          <Routes>
            <Route path="/broken" element={<BrokenChunk />} />
            <Route path="/good" element={<GoodChunk />} />
          </Routes>
        </Suspense>
      </RouteErrorBoundary>
    </MemoryRouter>
  )
}

describe('RouteErrorBoundary (S126 / W4)', () => {
  it('a rejected dynamic import renders a recovery affordance instead of blanking the page', async () => {
    render(<Shell />)

    await waitFor(() => expect(screen.getByRole('alert')).toBeInTheDocument())
    expect(screen.getByText('Siden kunne ikke indlæses')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Genindlæs siden' })).toBeInTheDocument()
  })

  it('keeps the shell mounted — the failure is scoped to the content region', async () => {
    render(<Shell />)

    await waitFor(() => expect(screen.getByRole('alert')).toBeInTheDocument())
    // Without the boundary React unmounts the whole tree and these disappear too. This is the
    // assertion that distinguishes "one region degraded" from "the app is gone".
    expect(screen.getByText('Skal blive stående')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Til god side' })).toBeInTheDocument()
  })

  it('does NOT swallow the cause — the error still reaches the console', async () => {
    render(<Shell />)
    await waitFor(() => expect(screen.getByRole('alert')).toBeInTheDocument())

    const logged = consoleErr.mock.calls.flat().map(String).join(' ')
    expect(logged).toContain('dynamically imported module')
  })

  it('RESETS on navigation — an error on one route does not persist to the next', async () => {
    const user = userEvent.setup()
    render(<Shell />)
    await waitFor(() => expect(screen.getByRole('alert')).toBeInTheDocument())

    await user.click(screen.getByRole('link', { name: 'Til god side' }))

    // A latching boundary would keep rendering the error here even though /good's chunk is fine —
    // the same permanent-dead-region bug in a milder form. This is the test for the one design
    // decision in this component that is not boilerplate.
    await waitFor(() => expect(screen.getByText('Virker fint')).toBeInTheDocument())
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('renders children untouched when nothing fails', async () => {
    render(<Shell initial="/good" />)
    await waitFor(() => expect(screen.getByText('Virker fint')).toBeInTheDocument())
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })
})
