// S72 / TASK-7203 — generic kit Drawer (SPRINT-72 R8): a fixed 440px right-side
// panel over a scrim. Scratch-built (no Radix primitive), so it carries the FULL
// overlay a11y set itself (Reviewer W6) — each a named test:
//   • focus trap (Tab/Shift+Tab wrap inside the drawer)
//   • Escape-to-close
//   • role="dialog" + aria-modal="true" + accessible name
//   • body scroll-lock while open
//   • focus-return-to-trigger on close
//   • 0.16s slide-in, dropped under prefers-reduced-motion (JS check + CSS belt)
// Visuals per the handoff: square corners, 1px left border, --shadow-sm, scrim.
import { useCallback, useEffect, useRef, type KeyboardEvent, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import styles from './Drawer.module.css'

interface DrawerProps {
  open: boolean
  onClose: () => void
  /** Accessible name for the dialog (aria-label) — required: the drawer has no
      intrinsic title element. */
  ariaLabel: string
  children: ReactNode
}

const FOCUSABLE_SELECTOR =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'

function prefersReducedMotion(): boolean {
  return (
    typeof window !== 'undefined' &&
    typeof window.matchMedia === 'function' &&
    window.matchMedia('(prefers-reduced-motion: reduce)').matches
  )
}

export function Drawer({ open, onClose, ariaLabel, children }: DrawerProps) {
  const drawerRef = useRef<HTMLDivElement | null>(null)

  // Scroll-lock + focus capture/return — tied to `open`. The previously focused
  // element (the trigger, e.g. a Registrér-arbejdstid grid cell) is captured
  // BEFORE we move focus into the drawer and restored on close/unmount.
  useEffect(() => {
    if (!open) return
    const trigger = document.activeElement instanceof HTMLElement ? document.activeElement : null
    const previousOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    // Initial focus: the drawer container itself (tabIndex -1) so Escape and the
    // trap engage immediately without surprise-focusing a form field.
    drawerRef.current?.focus()
    return () => {
      document.body.style.overflow = previousOverflow
      trigger?.focus()
    }
  }, [open])

  const handleKeyDown = useCallback(
    (e: KeyboardEvent<HTMLDivElement>) => {
      if (e.key === 'Escape') {
        e.stopPropagation()
        onClose()
        return
      }
      if (e.key !== 'Tab' || !drawerRef.current) return
      const focusables = Array.from(
        drawerRef.current.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR),
      )
      if (focusables.length === 0) {
        // Nothing focusable inside: keep focus on the container (full trap).
        e.preventDefault()
        return
      }
      const first = focusables[0]
      const last = focusables[focusables.length - 1]
      const active = document.activeElement
      if (e.shiftKey) {
        if (active === first || active === drawerRef.current) {
          e.preventDefault()
          last.focus()
        }
      } else if (active === last) {
        e.preventDefault()
        first.focus()
      }
    },
    [onClose],
  )

  if (!open) return null

  const drawerClass = prefersReducedMotion()
    ? styles.drawer
    : `${styles.drawer} ${styles.slideIn}`

  return createPortal(
    <>
      <div className={styles.scrim} onClick={onClose} aria-hidden="true" />
      <div
        ref={drawerRef}
        role="dialog"
        aria-modal="true"
        aria-label={ariaLabel}
        tabIndex={-1}
        className={drawerClass}
        onKeyDown={handleKeyDown}
      >
        {children}
      </div>
    </>,
    document.body,
  )
}
