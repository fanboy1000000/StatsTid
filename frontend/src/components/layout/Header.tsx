import { useAuth } from '../../contexts/AuthContext'
import { Badge } from '../ui/Badge'
import { Button } from '../ui/Button'
import styles from './Header.module.css'

export function Header() {
  const { user, role, orgId, logout } = useAuth()

  return (
    <header className={styles.header}>
      <h1 className={styles.title}>StatsTid</h1>
      <div className={styles.userSection}>
        {orgId && <span className={styles.orgLabel}>{orgId}</span>}
        <span className={styles.userId}>{user?.employeeId}</span>
        {role && <Badge variant="info">{role}</Badge>}
        <Button variant="ghost" size="sm" onClick={logout}>
          Log ud
        </Button>
      </div>
    </header>
  )
}
