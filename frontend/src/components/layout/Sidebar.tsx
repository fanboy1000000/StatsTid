import { NavLink } from 'react-router-dom'
import { useAuth } from '../../contexts/AuthContext'
import { hasMinRole } from '../../lib/roles'
import styles from './Sidebar.module.css'

interface NavItem {
  label: string
  to: string
}

const employeeItems: NavItem[] = [
  { label: 'Min Tid', to: '/' },
]

const leaderItems: NavItem[] = [
  { label: 'Godkendelser', to: '/approval' },
  { label: 'Vikariering', to: '/delegation' },
]

const hrItems: NavItem[] = [
  { label: 'Medarbejdere', to: '/admin/users' },
  { label: 'Medarbejderprofiler', to: '/admin/employee-profiles' },
  { label: 'Auditlog', to: '/admin/audit' },
]

const adminItems: NavItem[] = [
  { label: 'Organisation', to: '/admin/orgs' },
  { label: 'Projekter', to: '/admin/projects' },
  { label: 'Roller', to: '/admin/roles' },
  { label: 'Ledelseslinjer', to: '/admin/reporting-lines' },
  { label: 'Lokal konfiguration', to: '/config' },
]

const globalAdminItems: NavItem[] = [
  { label: 'Overenskomster', to: '/admin/agreements' },
  { label: 'Lønartstilknytninger', to: '/admin/wage-type-mappings' },
  { label: 'Positionstilpasninger', to: '/admin/position-overrides' },
]

function NavSection({ items }: { items: NavItem[] }) {
  return (
    <>
      {items.map((item) => (
        <NavLink
          key={item.to}
          to={item.to}
          end={item.to === '/'}
          className={({ isActive }) =>
            `${styles.navLink} ${isActive ? styles.navLinkActive : ''}`
          }
        >
          {item.label}
        </NavLink>
      ))}
    </>
  )
}

export function Sidebar() {
  const { role } = useAuth()

  const showLeader = hasMinRole(role, 'LocalLeader')
  const showHR = hasMinRole(role, 'LocalHR')
  const showAdmin = hasMinRole(role, 'LocalAdmin')
  const showGlobalAdmin = hasMinRole(role, 'GlobalAdmin')

  return (
    <aside className={styles.sidebar}>
      <nav className={styles.nav}>
        <NavSection items={employeeItems} />

        {showLeader && (
          <>
            <hr className={styles.sectionDivider} />
            <NavSection items={leaderItems} />
          </>
        )}

        {showHR && (
          <>
            <hr className={styles.sectionDivider} />
            <NavSection items={hrItems} />
          </>
        )}

        {showAdmin && (
          <>
            <hr className={styles.sectionDivider} />
            <NavSection items={adminItems} />
          </>
        )}

        {showGlobalAdmin && (
          <>
            <hr className={styles.sectionDivider} />
            <NavSection items={globalAdminItems} />
          </>
        )}
      </nav>
    </aside>
  )
}
