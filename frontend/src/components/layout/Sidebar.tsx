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
      { label: 'Godkendelser', to: '/godkend/godkendelser', minRole: 'LocalLeader' },
      { label: 'Vikariering', to: '/godkend/vikariering', minRole: 'LocalLeader' },
    ],
  },
  {
    prefix: '/admin',
    items: [
      { label: 'Medarbejdere', to: '/admin/medarbejdere', minRole: 'LocalHR' },
      { label: 'Audit log', to: '/admin/auditlog', minRole: 'LocalHR' },
      { label: 'Projekter', to: '/admin/projekter', minRole: 'LocalAdmin' },
      { label: 'Medarbejder administration', to: '/admin/ledelseslinjer', minRole: 'LocalAdmin' },
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
