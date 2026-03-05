export interface JwtPayload {
  sub: string
  role: string
  agreement_code: string
  org_id: string
  scopes: string // JSON string
  exp: number
  iat: number
}

export interface RoleScope {
  role: string
  orgId: string
  scopeType: 'GLOBAL' | 'ORG_ONLY' | 'ORG_AND_DESCENDANTS'
}

function base64UrlDecode(str: string): string {
  // Replace base64url chars with standard base64 chars
  let base64 = str.replace(/-/g, '+').replace(/_/g, '/')
  // Pad with '=' to make length a multiple of 4
  const pad = base64.length % 4
  if (pad) {
    base64 += '='.repeat(4 - pad)
  }
  return atob(base64)
}

export function decodeJwt(token: string): JwtPayload {
  const parts = token.split('.')
  if (parts.length !== 3) {
    throw new Error('Invalid JWT: expected 3 parts')
  }
  const payload = base64UrlDecode(parts[1])
  return JSON.parse(payload) as JwtPayload
}

export function parseScopes(scopesJson: string): RoleScope[] {
  try {
    const parsed: unknown = JSON.parse(scopesJson)
    if (!Array.isArray(parsed)) return []
    return parsed as RoleScope[]
  } catch {
    return []
  }
}

export function isTokenExpired(payload: JwtPayload): boolean {
  const nowSeconds = Math.floor(Date.now() / 1000)
  return payload.exp <= nowSeconds
}
