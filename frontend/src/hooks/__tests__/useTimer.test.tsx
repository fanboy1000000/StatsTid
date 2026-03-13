import { renderHook, waitFor } from '@testing-library/react'

// Mock apiClient
const mockGet = vi.fn()
const mockPost = vi.fn()

vi.mock('../../lib/api', () => ({
  apiClient: {
    get: (...args: unknown[]) => mockGet(...args),
    post: (...args: unknown[]) => mockPost(...args),
  },
}))

import { useTimer } from '../useTimer'

describe('useTimer', () => {
  beforeEach(() => {
    mockGet.mockReset()
    mockPost.mockReset()
  })

  it('returns null session when no active timer', async () => {
    mockGet.mockResolvedValueOnce({
      ok: true,
      data: {
        active: false,
        employeeId: 'EMP001',
        date: '2026-03-05',
        isActive: false,
        sessions: [],
      },
    })

    const { result } = renderHook(() => useTimer('EMP001'))

    await waitFor(() => {
      expect(result.current.loading).toBe(false)
    })

    expect(result.current.session).toBeNull()
    expect(result.current.elapsed).toBe('00:00:00')
  })

  it('updates elapsed time string when session is active', async () => {
    const checkInTime = new Date(Date.now() - 3661000).toISOString() // ~1h 1m 1s ago

    mockGet.mockResolvedValueOnce({
      ok: true,
      data: {
        active: true,
        sessionId: 'sess-1',
        employeeId: 'EMP001',
        date: '2026-03-05',
        checkInAt: checkInTime,
        isActive: true,
        sessions: [
          {
            sessionId: 'sess-1',
            checkInAt: checkInTime,
            checkOutAt: null,
            isActive: true,
          },
        ],
      },
    })

    const { result } = renderHook(() => useTimer('EMP001'))

    await waitFor(() => {
      expect(result.current.loading).toBe(false)
    })

    // Session should be active
    expect(result.current.session).not.toBeNull()
    expect(result.current.session?.isActive).toBe(true)

    // Elapsed should be a non-zero time string in HH:MM:SS format
    expect(result.current.elapsed).toMatch(/^\d{2}:\d{2}:\d{2}$/)
    // Should be at least 01:01:00 (approximately)
    expect(result.current.elapsed).not.toBe('00:00:00')
  })
})
