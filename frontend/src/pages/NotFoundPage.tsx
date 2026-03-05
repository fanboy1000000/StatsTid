import { Link } from 'react-router-dom'
import { Card } from '../components/ui/Card'
import styles from './NotFoundPage.module.css'

export function NotFoundPage() {
  return (
    <div className={styles.container}>
      <Card>
        <div className={styles.content}>
          <p className={styles.errorCode}>404</p>
          <h1 className={styles.title}>Siden blev ikke fundet</h1>
          <p className={styles.message}>
            Den side du leder efter findes ikke eller er blevet flyttet.
          </p>
          <Link to="/" className={styles.link}>
            Gaa til forsiden
          </Link>
        </div>
      </Card>
    </div>
  )
}
