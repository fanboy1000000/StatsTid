import { Outlet } from 'react-router-dom'
import { useAuth } from '../../contexts/AuthContext'
import { hasMinRole } from '../../lib/roles'
import { ForbiddenPage } from '../../pages/ForbiddenPage'

interface RequireRoleProps {
  minRole: string
}

export function RequireRole({ minRole }: RequireRoleProps) {
  const { role } = useAuth()

  if (!hasMinRole(role, minRole)) {
    return <ForbiddenPage />
  }

  return <Outlet />
}
