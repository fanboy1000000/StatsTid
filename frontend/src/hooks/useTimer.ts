import { useState, useEffect, useCallback, useRef } from 'react'
import { apiClient } from '../lib/api'
import type { TimerSession, TimerSessionEntry } from '../types'

export interface UseTimerResult {
  session: TimerSession | null
  loading: boolean
  checkIn: () => Promise<void>
  checkOut: () => Promise<void>
  elapsed: string
  sessions: TimerSessionEntry[]
  checkInClientTime: number | null
}

function formatElapsed(ms: number): string {
  const totalSeconds = Math.max(0, Math.floor(ms / 1000))
  const hours = Math.floor(totalSeconds / 3600)
  const minutes = Math.floor((totalSeconds % 3600) / 60)
  const seconds = totalSeconds % 60
  return `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`
}

interface TimerGetResponse {
  active: boolean
  sessionId?: string
  employeeId: string
  date: string
  checkInAt?: string
  isActive: boolean
  sessions?: TimerSessionEntry[]
}

interface CheckInResponse {
  sessionId: string
  employeeId: string
  date: string
  checkInAt: string
  isActive: boolean
}

interface CheckOutResponse {
  sessionId: string
  employeeId: string
  date: string
  checkInAt: string
  checkOutAt: string
  clockedHours: number
  isActive: boolean
}

export function useTimer(employeeId: string): UseTimerResult {
  const [session, setSession] = useState<TimerSession | null>(null)
  const [sessions, setSessions] = useState<TimerSessionEntry[]>([])
  const [loading, setLoading] = useState(true)
  const [elapsed, setElapsed] = useState('00:00:00')
  const [checkInClientTime, setCheckInClientTime] = useState<number | null>(null)
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null)

  const fetchSession = useCallback(async () => {
    setLoading(true)
    const result = await apiClient.get<TimerGetResponse>(`/api/timer/${employeeId}`)
    if (result.ok) {
      const data = result.data
      setSessions(data.sessions ?? [])
      if (data.active && data.checkInAt && data.sessionId) {
        setSession({
          sessionId: data.sessionId,
          employeeId: data.employeeId,
          date: data.date,
          checkInAt: data.checkInAt,
          checkOutAt: null,
          isActive: true,
          sessions: data.sessions,
        })
      } else {
        setSession(null)
      }
    }
    setLoading(false)
  }, [employeeId])

  useEffect(() => {
    fetchSession()
  }, [fetchSession])

  // Compute total elapsed from all sessions today
  useEffect(() => {
    if (intervalRef.current) {
      clearInterval(intervalRef.current)
      intervalRef.current = null
    }

    const computeTotal = () => {
      let totalMs = 0
      for (const s of sessions) {
        const start = new Date(s.checkInAt).getTime()
        const end = s.checkOutAt ? new Date(s.checkOutAt).getTime() : (s.isActive ? Date.now() : start)
        totalMs += end - start
      }
      setElapsed(formatElapsed(totalMs))
    }

    computeTotal()

    const hasActive = sessions.some(s => s.isActive)
    if (hasActive) {
      intervalRef.current = setInterval(computeTotal, 1000)
    }

    return () => {
      if (intervalRef.current) {
        clearInterval(intervalRef.current)
      }
    }
  }, [sessions])

  const checkIn = useCallback(async () => {
    setLoading(true)
    const clientTime = Date.now()
    const result = await apiClient.post<CheckInResponse>('/api/timer/check-in', { employeeId })
    if (result.ok) {
      const data = result.data
      setCheckInClientTime(clientTime)
      // Update state directly from POST response — no redundant GET
      const newEntry: TimerSessionEntry = {
        sessionId: data.sessionId,
        checkInAt: data.checkInAt,
        checkOutAt: null,
        isActive: true,
      }
      setSessions(prev => [...prev, newEntry])
      setSession({
        sessionId: data.sessionId,
        employeeId: data.employeeId,
        date: data.date,
        checkInAt: data.checkInAt,
        checkOutAt: null,
        isActive: true,
      })
    }
    setLoading(false)
  }, [employeeId])

  const checkOut = useCallback(async () => {
    setLoading(true)
    const result = await apiClient.post<CheckOutResponse>('/api/timer/check-out', { employeeId })
    if (result.ok) {
      const data = result.data
      setCheckInClientTime(null)
      // Update state directly from POST response — no redundant GET
      setSession(null)
      setSessions(prev => prev.map(s =>
        s.sessionId === data.sessionId
          ? { ...s, checkOutAt: data.checkOutAt, isActive: false }
          : s
      ))
    }
    setLoading(false)
  }, [employeeId])

  return { session, loading, checkIn, checkOut, elapsed, sessions, checkInClientTime }
}
