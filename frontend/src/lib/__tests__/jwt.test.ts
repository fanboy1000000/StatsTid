import { decodeJwt, parseScopes, isTokenExpired } from '../jwt'

// Helper to create a JWT token with given payload
function makeToken(payload: Record<string, unknown>): string {
  const header = btoa(JSON.stringify({ alg: 'HS256', typ: 'JWT' }))
  const body = btoa(JSON.stringify(payload))
  return `${header}.${body}.fakesignature`
}

describe('decodeJwt', () => {
  it('decodes a valid JWT payload', () => {
    const payload = {
      sub: 'user1',
      role: 'Employee',
      agreement_code: 'HK',
      org_id: 'ORG1',
      scopes: '[]',
      exp: 9999999999,
      iat: 1000000000,
    }
    const token = makeToken(payload)
    const result = decodeJwt(token)
    expect(result.sub).toBe('user1')
    expect(result.role).toBe('Employee')
    expect(result.org_id).toBe('ORG1')
    expect(result.agreement_code).toBe('HK')
  })

  it('throws on invalid JWT (not 3 parts)', () => {
    expect(() => decodeJwt('invalid')).toThrow('Invalid JWT: expected 3 parts')
    expect(() => decodeJwt('a.b')).toThrow('Invalid JWT: expected 3 parts')
  })
})

describe('parseScopes', () => {
  it('parses valid scopes JSON', () => {
    const scopes = JSON.stringify([
      { role: 'LocalAdmin', orgId: 'ORG1', scopeType: 'ORG_AND_DESCENDANTS' },
    ])
    const result = parseScopes(scopes)
    expect(result).toHaveLength(1)
    expect(result[0].role).toBe('LocalAdmin')
    expect(result[0].orgId).toBe('ORG1')
  })

  it('returns empty array for invalid JSON', () => {
    expect(parseScopes('not-json')).toEqual([])
  })

  it('returns empty array for non-array JSON', () => {
    expect(parseScopes('{}')).toEqual([])
  })
})

describe('isTokenExpired', () => {
  it('returns false for future expiry', () => {
    const payload = { sub: '', role: '', agreement_code: '', org_id: '', scopes: '', exp: 9999999999, iat: 0 }
    expect(isTokenExpired(payload)).toBe(false)
  })

  it('returns true for past expiry', () => {
    const payload = { sub: '', role: '', agreement_code: '', org_id: '', scopes: '', exp: 1000000000, iat: 0 }
    expect(isTokenExpired(payload)).toBe(true)
  })
})
