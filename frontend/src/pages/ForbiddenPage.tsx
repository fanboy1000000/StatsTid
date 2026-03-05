import { Link } from 'react-router-dom'
import { Card } from '../components/ui/Card'
import styles from './ForbiddenPage.module.css'

export function ForbiddenPage() {
  return (
    <div className={styles.container}>
      <Card>
        <div className={styles.content}>
          <p className={styles.errorCode}>403</p>
          <h1 className={styles.title}>Adgang naegtet</h1>
          <p className={styles.message}>
            Du har ikke tilstraekkelige rettigheder til at se denne side.
          </p>
          <Link to="/" className={styles.link}>
            Gaa til forsiden
          </Link>
        </div>
      </Card>
    </div>
  )
}
