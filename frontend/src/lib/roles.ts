export const ROLE_LEVELS: Record<string, number> = {
  GlobalAdmin: 1,
  LocalAdmin: 2,
  LocalHR: 3,
  LocalLeader: 4,
  Employee: 5,
}

export function hasMinRole(userRole: string | null, minRole: string): boolean {
  if (!userRole) return false
  return (ROLE_LEVELS[userRole] ?? 99) <= (ROLE_LEVELS[minRole] ?? 0)
}
