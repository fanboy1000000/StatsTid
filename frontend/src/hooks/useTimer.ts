import { useState, useEffect, useCallback, useRef } from 'react'
import { apiClient } from '../lib/api'
import type { TimerSession } from '../types'

interface UseTimerResult {
  session: TimerSession | null
  loading: boolean
  checkIn: () => Promise<void>
  checkOut: () => Promise<void>
  elapsed: string
}

function formatElapsed(ms: number): string {
  const totalSeconds = Math.max(0, Math.floor(ms / 1000))
  const hours = Math.floor(totalSeconds / 3600)
  const minutes = Math.floor((totalSeconds % 3600) / 60)
  const seconds = totalSeconds % 60
  return `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`
}

export function useTimer(employeeId: string): UseTimerResult {
  const [session, setSession] = useState<TimerSession | null>(null)
  const [loading, setLoading] = useState(true)
  const [elapsed, setElapsed] = useState('00:00:00')
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null)

  const fetchSession = useCallback(async () => {
    setLoading(true)
    const result = await apiClient.get<TimerSession | null>(`/api/timer/${employeeId}`)
    if (result.ok) {
      setSession(result.data)
    }
    setLoading(false)
  }, [employeeId])

  useEffect(() => {
    fetchSession()
  }, [fetchSession])

  // Update elapsed time every second when session is active
  useEffect(() => {
    if (intervalRef.current) {
      clearInterval(intervalRef.current)
      intervalRef.current = null
    }

    if (session?.isActive && session.checkInAt) {
      const updateElapsed = () => {
        const checkInTime = new Date(session.checkInAt).getTime()
        const now = Date.now()
        setElapsed(formatElapsed(now - checkInTime))
      }
      updateElapsed()
      intervalRef.current = setInterval(updateElapsed, 1000)
    } else if (session?.checkInAt && session.checkOutAt) {
      const checkInTime = new Date(session.checkInAt).getTime()
      const checkOutTime = new Date(session.checkOutAt).getTime()
      setElapsed(formatElapsed(checkOutTime - checkInTime))
    } else {
      setElapsed('00:00:00')
    }

    return () => {
      if (intervalRef.current) {
        clearInterval(intervalRef.current)
      }
    }
  }, [session])

  const checkIn = useCallback(async () => {
    setLoading(true)
    const result = await apiClient.post<TimerSession>('/api/timer/check-in', { employeeId })
    if (result.ok) {
      setSession(result.data)
    }
    setLoading(false)
  }, [employeeId])

  const checkOut = useCallback(async () => {
    setLoading(true)
    const result = await apiClient.post<TimerSession>('/api/timer/check-out', { employeeId })
    if (result.ok) {
      setSession(result.data)
    }
    setLoading(false)
  }, [employeeId])

  return { session, loading, checkIn, checkOut, elapsed }
}
