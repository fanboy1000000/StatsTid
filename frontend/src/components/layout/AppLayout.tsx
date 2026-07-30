import { Suspense } from 'react'
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
            {/* S125 / TASK-12504 (F3): the route chunks load here, INSIDE the shell, so the
                header/nav/sidebar stay put and only this region swaps — the page never blanks.
                The fallback is deliberately a non-blocking placeholder rather than a spinner:
                a spinner that appears for 40 ms reads as a flash of broken UI, which is the F6
                perception problem this must not make worse. */}
            <Suspense fallback={<div className={styles.routeFallback} aria-busy="true" aria-live="polite" />}>
              <Outlet />
            </Suspense>
          </div>
        </main>
      </div>
    </div>
  )
}
