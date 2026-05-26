import { Outlet } from 'react-router-dom'
import { Header } from './Header'
import { TopNav } from './TopNav'
import { Sidebar } from './Sidebar'
import styles from './AppLayout.module.css'

export function AppLayout() {
  return (
    <div className={styles.layoutRoot}>
      <Header />
      <TopNav />
      <div className={styles.body}>
        <Sidebar />
        <main className={styles.main}>
          <div className={styles.mainInner}>
            <Outlet />
          </div>
        </main>
      </div>
    </div>
  )
}
