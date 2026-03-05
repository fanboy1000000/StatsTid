import type { TimerSession } from '../types'
import { Button } from './ui/Button'
import styles from './TimerControl.module.css'

interface TimerControlProps {
  session: TimerSession | null
  elapsed: string
  onCheckIn: () => void
  onCheckOut: () => void
  loading: boolean
}

function formatTime(isoString: string | null | undefined): string {
  if (!isoString) return '--:--'
  try {
    const date = new Date(isoString)
    return `${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`
  } catch {
    return '--:--'
  }
}

export function TimerControl({ session, elapsed, onCheckIn, onCheckOut, loading }: TimerControlProps) {
  const isActive = session?.isActive ?? false

  return (
    <div className={styles.container}>
      <div className={styles.timerDisplay}>
        <span className={styles.elapsedLabel}>Tid:</span>
        <span className={styles.elapsedValue}>{elapsed}</span>
      </div>

      <div className={styles.times}>
        <span className={styles.timeEntry}>
          <span className={styles.timeLabel}>Ankomst:</span>
          <span className={styles.timeValue}>{formatTime(session?.checkInAt)}</span>
        </span>
        <span className={styles.timeEntry}>
          <span className={styles.timeLabel}>Afgang:</span>
          <span className={styles.timeValue}>{formatTime(session?.checkOutAt)}</span>
        </span>
      </div>

      <div className={styles.action}>
        {isActive ? (
          <Button variant="danger" size="sm" onClick={onCheckOut} disabled={loading}>
            Tjek ud
          </Button>
        ) : (
          <Button variant="primary" size="sm" onClick={onCheckIn} disabled={loading}>
            Tjek ind
          </Button>
        )}
      </div>
    </div>
  )
}
