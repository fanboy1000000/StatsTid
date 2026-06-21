import { NavLink, useLocation } from 'react-router-dom'
import { useAuth } from '../../contexts/AuthContext'
import { hasMinRole } from '../../lib/roles'
import styles from './TopNav.module.css'

interface TabDef {
  label: string
  minRole: string | null
  firstRoute: string
  prefix: string
}

const TABS: TabDef[] = [
  { label: 'Min tid', minRole: null, firstRoute: '/tid/registrering', prefix: '/tid' },
  { label: 'Godkend tid', minRole: 'LocalLeader', firstRoute: '/godkend/oversigt', prefix: '/godkend' },
  { label: 'Administration', minRole: 'LocalHR', firstRoute: '/admin/ledelseslinjer', prefix: '/admin' },
  { label: 'Lokale tilpasninger', minRole: 'LocalAdmin', firstRoute: '/lokal/ok-konfiguration', prefix: '/lokal' },
  { label: 'Global administration', minRole: 'GlobalAdmin', firstRoute: '/global/overenskomster', prefix: '/global' },
]

export function TopNav() {
  const { role } = useAuth()
  const { pathname } = useLocation()

  const visibleTabs = TABS.filter(
    (tab) => tab.minRole === null || hasMinRole(role, tab.minRole)
  )

  return (
    <nav aria-label="Hovednavigation" className={styles.nav}>
      <div className={styles.tabList}>
        {visibleTabs.map((tab) => {
          const isActive = pathname.startsWith(tab.prefix)
          return (
            <NavLink
              key={tab.prefix}
              to={tab.firstRoute}
              aria-current={isActive ? 'page' : undefined}
              className={`${styles.tab}${isActive ? ` ${styles.tabActive}` : ''}`}
            >
              {tab.label}
            </NavLink>
          )
        })}
      </div>
    </nav>
  )
}
