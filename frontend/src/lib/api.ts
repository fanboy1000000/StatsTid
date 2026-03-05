const TOKEN_KEY = 'statstid_token'
const USER_KEY = 'statstid_user'

export type ApiResult<T> = { ok: true; data: T } | { ok: false; error: string; status: number }

function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY)
}

function handle401() {
  localStorage.removeItem(TOKEN_KEY)
  localStorage.removeItem(USER_KEY)
  window.location.reload()
}

async function request<T>(method: string, path: string, body?: unknown): Promise<ApiResult<T>> {
  const token = getToken()
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
  }
  if (token) {
    headers['Authorization'] = `Bearer ${token}`
  }

  try {
    const res = await fetch(path, {
      method,
      headers,
      body: body !== undefined ? JSON.stringify(body) : undefined,
    })

    if (res.status === 401) {
      handle401()
      return { ok: false, error: 'Unauthorized', status: 401 }
    }

    if (!res.ok) {
      let errorMessage = `HTTP ${res.status}`
      try {
        const errorBody = await res.text()
        if (errorBody) {
          errorMessage = errorBody
        }
      } catch {
        // Use default error message
      }
      return { ok: false, error: errorMessage, status: res.status }
    }

    // Handle 204 No Content
    if (res.status === 204) {
      return { ok: true, data: undefined as T }
    }

    const data = await res.json() as T
    return { ok: true, data }
  } catch (e) {
    return { ok: false, error: String(e), status: 0 }
  }
}

export const apiClient = {
  get<T>(path: string): Promise<ApiResult<T>> {
    return request<T>('GET', path)
  },
  post<T>(path: string, body?: unknown): Promise<ApiResult<T>> {
    return request<T>('POST', path, body)
  },
  put<T>(path: string, body?: unknown): Promise<ApiResult<T>> {
    return request<T>('PUT', path, body)
  },
  delete<T>(path: string): Promise<ApiResult<T>> {
    return request<T>('DELETE', path)
  },
}
