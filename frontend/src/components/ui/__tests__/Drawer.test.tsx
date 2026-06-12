// S72 / TASK-7203 — kit Drawer overlay-a11y NAMED pins (SPRINT-72 R8 / Reviewer
// W6): the Drawer is scratch-built (no Radix primitive), so the full overlay
// a11y set — focus trap, Escape-to-close, role/aria, scroll-lock,
// focus-return-to-trigger, reduced-motion — must each be proven here.
import { render, fireEvent, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { Drawer } from '../Drawer'

function renderDrawer(overrides: { open?: boolean; onClose?: () => void } = {}) {
  const onClose = overrides.onClose ?? vi.fn()
  const result = render(
    <Drawer open={overrides.open ?? true} onClose={onClose} ariaLabel="Registrér tid — test">
      <button type="button">først</button>
      <input id="midt" aria-label="midt" />
      <button type="button">sidst</button>
    </Drawer>,
  )
  return { ...result, onClose }
}

/** Open/close harness with an external trigger button (focus-return pin). */
function Harness() {
  const [open, setOpen] = useState(false)
  return (
    <>
      <button type="button" onClick={() => setOpen(true)}>
        åbn
      </button>
      <Drawer open={open} onClose={() => setOpen(false)} ariaLabel="test-drawer">
        <button type="button">inde</button>
      </Drawer>
    </>
  )
}

afterEach(() => {
  vi.unstubAllGlobals()
  document.body.style.overflow = ''
})

describe('Drawer — R8 overlay a11y (named pins)', () => {
  it('R8: renders role="dialog" with aria-modal="true" and the accessible name', () => {
    renderDrawer()
    const dialog = screen.getByRole('dialog')
    expect(dialog).toHaveAttribute('aria-modal', 'true')
    expect(dialog).toHaveAccessibleName('Registrér tid — test')
  })

  it('R8: Escape-to-close — pressing Escape inside the drawer calls onClose', () => {
    const { onClose } = renderDrawer()
    fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape' })
    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('R8: focus trap — Tab on the last focusable wraps to the first, Shift+Tab on the first wraps to the last', () => {
    renderDrawer()
    const first = screen.getByRole('button', { name: 'først' })
    const last = screen.getByRole('button', { name: 'sidst' })

    last.focus()
    fireEvent.keyDown(last, { key: 'Tab' })
    expect(first).toHaveFocus()

    first.focus()
    fireEvent.keyDown(first, { key: 'Tab', shiftKey: true })
    expect(last).toHaveFocus()
  })

  it('R8: scroll-lock — body scrolling locks while open and is restored on close', () => {
    const onClose = vi.fn()
    const { rerender } = render(
      <Drawer open onClose={onClose} ariaLabel="lås">
        <button type="button">x</button>
      </Drawer>,
    )
    expect(document.body.style.overflow).toBe('hidden')
    rerender(
      <Drawer open={false} onClose={onClose} ariaLabel="lås">
        <button type="button">x</button>
      </Drawer>,
    )
    expect(document.body.style.overflow).toBe('')
  })

  it('R8: focus-return-to-trigger — focus returns to the previously-focused element on close', async () => {
    const user = userEvent.setup()
    render(<Harness />)
    const trigger = screen.getByRole('button', { name: 'åbn' })

    await user.click(trigger) // click focuses the trigger, then opens the drawer
    const dialog = screen.getByRole('dialog')
    expect(dialog).toHaveFocus() // initial focus moved INTO the drawer

    fireEvent.keyDown(dialog, { key: 'Escape' })
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(trigger).toHaveFocus()
  })

  it('R8: reduced-motion — the 0.16s slide animation class is dropped under prefers-reduced-motion', () => {
    vi.stubGlobal(
      'matchMedia',
      ((query: string) => ({ matches: query.includes('prefers-reduced-motion'), media: query })) as unknown as typeof window.matchMedia,
    )
    renderDrawer()
    expect(screen.getByRole('dialog').className).not.toContain('slideIn')
  })

  it('plays the 0.16s slide-in by default (no reduced-motion preference)', () => {
    renderDrawer()
    expect(screen.getByRole('dialog').className).toContain('slideIn')
  })

  it('clicking the scrim closes the drawer', () => {
    const { onClose } = renderDrawer()
    const scrim = document.querySelector('.scrim') as HTMLElement
    expect(scrim).toBeTruthy()
    expect(scrim).toHaveAttribute('aria-hidden', 'true')
    fireEvent.click(scrim)
    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('renders nothing when closed', () => {
    renderDrawer({ open: false })
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(document.querySelector('.scrim')).toBeNull()
  })
})
