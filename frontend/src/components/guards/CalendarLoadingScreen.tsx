import { Spinner } from '../ui/Spinner'
import styles from './CalendarLoadingScreen.module.css'

/**
 * S143 / TASK-14301 — the app shell's FIRST paint while the bootstrap read
 * (`GET /api/calendar/today`) is in flight. Owner ruling OQ-1d: nothing else
 * renders — not the header, not the nav — until the server's day is
 * confirmed, so this is deliberately the ONLY thing on screen rather than a
 * skeleton of the layout underneath it.
 */
export function CalendarLoadingScreen() {
  return (
    <div className={styles.container} aria-busy="true" aria-live="polite">
      <Spinner size="lg" />
      <p className={styles.message}>Henter dags dato…</p>
    </div>
  )
}
