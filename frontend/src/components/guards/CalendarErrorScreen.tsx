import { Card } from '../ui/Card'
import { Button } from '../ui/Button'
import styles from './CalendarErrorScreen.module.css'

interface CalendarErrorScreenProps {
  onRetry: () => void
}

/**
 * S143 / TASK-14301 — rendered when the calendar bootstrap read
 * (`GET /api/calendar/today`) fails for a reason OTHER than an expired
 * session (a 401 routes to `/login` instead — see `RequireAuth.tsx`). Owner
 * ruling OQ-1d accepts the cost that a transient failure here makes the
 * product unusable even for reading: the alternative is falling back to the
 * device's clock at exactly the moment — a bad read — where that is wrong.
 *
 * "Prøv igen" re-runs the SAME bootstrap read (`useCalendarBootstrap`'s
 * `retry`) rather than a full page reload — cheaper, and it is what a test
 * exercises without having to stub `window.location`.
 */
export function CalendarErrorScreen({ onRetry }: CalendarErrorScreenProps) {
  return (
    <div className={styles.container} role="alert">
      <Card>
        <div className={styles.content}>
          <h1 className={styles.title}>Dags dato kunne ikke hentes</h1>
          <p className={styles.message}>
            Systemet kunne ikke bekræfte dagens dato hos serveren. Uden den kan tid ikke registreres
            korrekt, så siden er midlertidigt utilgængelig.
          </p>
          <Button onClick={onRetry}>Prøv igen</Button>
        </div>
      </Card>
    </div>
  )
}
