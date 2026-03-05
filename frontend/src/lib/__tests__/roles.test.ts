import { ROLE_LEVELS, hasMinRole } from '../roles'

describe('ROLE_LEVELS', () => {
  it('has correct hierarchy ordering', () => {
    expect(ROLE_LEVELS['GlobalAdmin']).toBe(1)
    expect(ROLE_LEVELS['LocalAdmin']).toBe(2)
    expect(ROLE_LEVELS['LocalHR']).toBe(3)
    expect(ROLE_LEVELS['LocalLeader']).toBe(4)
    expect(ROLE_LEVELS['Employee']).toBe(5)
  })
})

describe('hasMinRole', () => {
  it('returns true when user role meets minimum', () => {
    expect(hasMinRole('GlobalAdmin', 'Employee')).toBe(true)
    expect(hasMinRole('GlobalAdmin', 'GlobalAdmin')).toBe(true)
    expect(hasMinRole('LocalAdmin', 'LocalAdmin')).toBe(true)
    expect(hasMinRole('Employee', 'Employee')).toBe(true)
  })

  it('returns false when user role is below minimum', () => {
    expect(hasMinRole('Employee', 'LocalLeader')).toBe(false)
    expect(hasMinRole('LocalLeader', 'LocalAdmin')).toBe(false)
    expect(hasMinRole('LocalHR', 'LocalAdmin')).toBe(false)
  })

  it('returns false when user role is null', () => {
    expect(hasMinRole(null, 'Employee')).toBe(false)
  })

  it('returns false for unknown roles', () => {
    expect(hasMinRole('UnknownRole', 'Employee')).toBe(false)
  })
})
