import { NavLink, useLocation } from 'react-router-dom'
import { useAuth } from '../../contexts/AuthContext'
import { hasMinRole } from '../../lib/roles'
import styles from './Sidebar.module.css'

interface MenuItem {
  label: string
  to: string
  minRole: string | null
}

interface TabGroup {
  prefix: string
  items: MenuItem[]
}

const tabGroups: TabGroup[] = [
  {
    prefix: '/tid',
    items: [
      { label: 'Registrering', to: '/tid/registrering', minRole: null },
      { label: 'Oversigt', to: '/tid/oversigt', minRole: null },
    ],
  },
  {
    prefix: '/godkend',
    items: [
      // S87 / TASK-8702 (OQ-3): approvals merged into the Teamoversigt. The old
      // "Godkendelser" item is removed; "Oversigt" leads the group.
      { label: 'Oversigt', to: '/godkend/oversigt', minRole: 'LocalLeader' },
      { label: 'Vikariering', to: '/godkend/vikariering', minRole: 'LocalLeader' },
    ],
  },
  {
    prefix: '/admin',
    items: [
      { label: 'Medarbejder administration', to: '/admin/ledelseslinjer', minRole: 'LocalHR' },
      // S107 / TASK-10701: temporary LocalHR entry so the merged "Organisation &
      // medarbejdere" page (Enhedsspor VIEW half) is reachable during the
      // S107→S108 interim. The two old entries (Medarbejder administration above,
      // Global → Organisation) stay until the S108 cutover collapses to one.
      { label: 'Organisation & medarbejdere', to: '/admin/organisation-medarbejdere', minRole: 'LocalHR' },
      { label: 'Audit log', to: '/admin/auditlog', minRole: 'LocalHR' },
      { label: 'Projekter', to: '/admin/projekter', minRole: 'LocalAdmin' },
      { label: 'Brugerrettigheder', to: '/admin/brugerrettigheder', minRole: 'LocalAdmin' },
    ],
  },
  {
    prefix: '/lokal',
    items: [
      { label: 'Lokal OK konfiguration', to: '/lokal/ok-konfiguration', minRole: 'LocalAdmin' },
      { label: 'Lokale stillingstilpasninger', to: '/lokal/stillingstilpasninger', minRole: 'LocalAdmin' },
    ],
  },
  {
    prefix: '/global',
    items: [
      { label: 'Overenskomster', to: '/global/overenskomster', minRole: 'GlobalAdmin' },
      { label: 'Organisation', to: '/global/organisation', minRole: 'GlobalAdmin' },
      { label: 'Lønartstilknytning', to: '/global/loenartstilknytning', minRole: 'GlobalAdmin' },
    ],
  },
]

export function Sidebar() {
  const { role } = useAuth()
  const { pathname } = useLocation()

  const activeGroup = tabGroups.find((g) => pathname.startsWith(g.prefix))
  if (!activeGroup) {
    return (
      <aside className={styles.sidebar}>
        <nav className={styles.nav} />
      </aside>
    )
  }

  const visibleItems = activeGroup.items.filter(
    (item) => item.minRole === null || hasMinRole(role, item.minRole),
  )

  return (
    <aside className={styles.sidebar}>
      <nav className={styles.nav}>
        {visibleItems.map((item) => (
          <NavLink
            key={item.to}
            to={item.to}
            end={item.to === '/tid/registrering'}
            className={({ isActive }) =>
              `${styles.navLink} ${isActive ? styles.navLinkActive : ''}`
            }
          >
            {item.label}
          </NavLink>
        ))}
      </nav>
    </aside>
  )
}
