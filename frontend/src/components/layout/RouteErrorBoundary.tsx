import { Component, type ErrorInfo, type ReactNode } from 'react'
import { useLocation } from 'react-router-dom'
import styles from './AppLayout.module.css'

/**
 * S126 / W4 — the error boundary the F3 route split needed and did not get.
 *
 * <b>The regression this closes.</b> S125 / TASK-12504 converted 16 page imports to
 * `React.lazy(() => import(...))`. Before that every page lived in one already-loaded bundle, so a
 * page could not fail to *arrive* — only to render. After it, a dynamic import can REJECT: a user
 * sitting on a cached `index.html` after a redeploy requests a chunk whose hashed filename no longer
 * exists, or a transient network blip drops the request. React then throws during render, unmounts
 * the tree, and the user is left with a **permanently blank page** until they reload by hand. Nothing
 * in `frontend/src` caught that — `lazy-routes.spec.ts` listens for exactly this error class, so it
 * was understood at the time and left unhandled.
 *
 * <b>Why it wraps the Suspense rather than the router.</b> Same reasoning as F3's Suspense placement:
 * the header, top nav and sidebar stay mounted, so a failed chunk degrades ONE region instead of the
 * whole window. The user keeps their navigation and can move elsewhere without touching the browser.
 *
 * <b>Why it resets on navigation (the part that is not boilerplate).</b> A boundary that latches stays
 * broken for the rest of the session: once tripped, every subsequent route renders the fallback even
 * though those chunks are fine — a milder version of the bug it was added to fix. Remounting on
 * `location.pathname` (see {@link RouteErrorBoundary}) clears the error state on every navigation, so
 * the blast radius is the one route that actually failed.
 *
 * <b>Why the affordance is a reload and not a retry.</b> The dominant cause is a stale document
 * referencing chunk names that no longer exist. Re-invoking the same failed import would request the
 * same dead URL; only re-fetching `index.html` gets the new manifest. A "try again" button that
 * cannot work is worse than none.
 */
interface Props {
  children: ReactNode
}

interface State {
  error: Error | null
}

class RouteErrorBoundaryInner extends Component<Props, State> {
  state: State = { error: null }

  static getDerivedStateFromError(error: Error): State {
    return { error }
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    // Deliberately console.error rather than swallowing: the e2e chunk-error listener keys on this,
    // and a boundary that hides the cause makes the next failure harder to diagnose than no boundary.
    console.error('Route render failed', error, info.componentStack)
  }

  render() {
    if (!this.state.error) return this.props.children

    return (
      <div className={styles.routeError} role="alert">
        <h2 className={styles.routeErrorTitle}>Siden kunne ikke indlæses</h2>
        <p className={styles.routeErrorBody}>
          Der opstod en fejl under indlæsning af denne side. Det sker typisk, hvis systemet er blevet
          opdateret, mens du havde fanen åben.
        </p>
        <button
          type="button"
          className={styles.routeErrorBtn}
          onClick={() => window.location.reload()}
        >
          Genindlæs siden
        </button>
      </div>
    )
  }
}

/**
 * Remounts the boundary on every navigation via `key`, so an error on one route does not persist
 * across the next one. `useLocation` is why this wrapper exists at all — hooks are unavailable in the
 * class component that must implement `getDerivedStateFromError`.
 */
export function RouteErrorBoundary({ children }: Props) {
  const location = useLocation()
  return <RouteErrorBoundaryInner key={location.pathname}>{children}</RouteErrorBoundaryInner>
}
