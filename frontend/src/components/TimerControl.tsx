import { useState, useEffect, useRef, useMemo } from 'react'
import type { TimerSession } from '../types'
import type { WorkInterval } from './SkemaGrid'
import { Button } from './ui/Button'
import styles from './TimerControl.module.css'

interface TimerControlProps {
  session: TimerSession | null
  onCheckIn: () => void
  onCheckOut: () => void
  loading: boolean
  todayIntervals?: WorkInterval[]
  checkInClientTime?: number | null
}

function calcTotalMs(intervals: WorkInterval[]): number {
  let totalMs = 0
  for (const iv of intervals) {
    if (iv.start && iv.end) {
      const sp = iv.start.split(':').map(Number)
      const ep = iv.end.split(':').map(Number)
      const startSec = sp[0] * 3600 + sp[1] * 60 + (sp[2] ?? 0)
      const endSec = ep[0] * 3600 + ep[1] * 60 + (ep[2] ?? 0)
      const diff = endSec - startSec
      if (diff > 0) totalMs += diff * 1000
    }
  }
  return totalMs
}

function formatElapsed(ms: number): string {
  const totalSeconds = Math.max(0, Math.floor(ms / 1000))
  const hours = Math.floor(totalSeconds / 3600)
  const minutes = Math.floor((totalSeconds % 3600) / 60)
  const seconds = totalSeconds % 60
  return `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`
}

export function TimerControl({ session, onCheckIn, onCheckOut, loading, todayIntervals = [], checkInClientTime }: TimerControlProps) {
  const isActive = session?.isActive ?? false
  const [displayElapsed, setDisplayElapsed] = useState('00:00:00')
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null)
  // Snapshot the base total (completed intervals) when check-in starts ticking
  const activeEpochRef = useRef<{ clientTime: number; base: number } | null>(null)

  useEffect(() => {
    if (intervalRef.current) {
      clearInterval(intervalRef.current)
      intervalRef.current = null
    }

    if (isActive) {
      // Set up epoch on first active render (or when checkInClientTime changes)
      if (!activeEpochRef.current) {
        // Base = total of all completed intervals (the active one has start===end, contributing 0)
        const base = calcTotalMs(todayIntervals)
        if (checkInClientTime) {
          // Fresh check-in this session — use client timestamp (no server/client time mixing)
          activeEpochRef.current = { clientTime: checkInClientTime, base }
        } else {
          // Page load with existing active session — use server timestamp via intervals
          // Fall back to server-time computation (small GET round-trip inflation, acceptable)
          activeEpochRef.current = { clientTime: Date.now(), base: calcTotalFromServer(todayIntervals, session) }
        }
      }

      const compute = () => {
        const epoch = activeEpochRef.current!
        const elapsed = Date.now() - epoch.clientTime
        setDisplayElapsed(formatElapsed(epoch.base + elapsed))
      }

      compute()
      intervalRef.current = setInterval(compute, 1000)
    } else {
      // Inactive — compute purely from intervals
      activeEpochRef.current = null
      setDisplayElapsed(formatElapsed(calcTotalMs(todayIntervals)))
    }

    return () => {
      if (intervalRef.current) clearInterval(intervalRef.current)
    }
  }, [todayIntervals, isActive, session?.checkInAt, checkInClientTime])

  const intervalsSummary = useMemo(() => {
    return todayIntervals
      .filter(iv => iv.start && iv.end)
      .map(iv => `${iv.start} – ${iv.end}`)
  }, [todayIntervals])

  return (
    <div className={styles.container}>
      <div className={styles.timerDisplay}>
        <span className={styles.elapsedLabel}>Tid i dag:</span>
        <span className={styles.elapsedValue}>{displayElapsed}</span>
      </div>

      {intervalsSummary.length > 0 && (
        <div className={styles.sessionsList}>
          {intervalsSummary.map((s, i) => (
            <span key={i} className={styles.sessionEntry}>
              {s}{i < intervalsSummary.length - 1 ? ', ' : ''}
            </span>
          ))}
        </div>
      )}

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

// For page-load case: compute total including active session from server timestamps
function calcTotalFromServer(intervals: WorkInterval[], session: TimerSession | null): number {
  let totalMs = calcTotalMs(intervals)
  // The active interval has start===end (0 contribution). Add live elapsed from server checkInAt.
  if (session?.isActive && session.checkInAt) {
    const checkIn = new Date(session.checkInAt).getTime()
    totalMs += Date.now() - checkIn
  }
  return totalMs
}
