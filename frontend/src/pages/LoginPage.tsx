import { useState, type FormEvent } from 'react'
import { Card, FormField, Input, Button, Alert } from '../components/ui'
import styles from './LoginPage.module.css'

interface Props {
  onLogin: (username: string, password: string) => Promise<void>
}

export function LoginPage({ onLogin }: Props) {
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()
    setError(null)
    setLoading(true)
    try {
      await onLogin(username, password)
    } catch (err) {
      setError(String(err instanceof Error ? err.message : err))
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className={styles.wrapper}>
      <div className={styles.container}>
        <h1 className={styles.title}>StatsTid</h1>
        <h2 className={styles.subtitle}>Log ind</h2>
        <Card>
          <form onSubmit={handleSubmit} className={styles.form}>
            <FormField label="Brugernavn" htmlFor="username" required>
              <Input
                id="username"
                type="text"
                value={username}
                onChange={e => setUsername(e.target.value)}
                required
              />
            </FormField>
            <FormField label="Adgangskode" htmlFor="password" required>
              <Input
                id="password"
                type="password"
                value={password}
                onChange={e => setPassword(e.target.value)}
                required
              />
            </FormField>
            {error && <Alert variant="error">{error}</Alert>}
            <div className={styles.actions}>
              <Button type="submit" disabled={loading}>
                {loading ? 'Logger ind...' : 'Log ind'}
              </Button>
            </div>
          </form>
        </Card>
        <div className={styles.hint}>
          <p>Test brugernavne: admin01, mgr01, emp001, emp002, emp003, readonly01</p>
        </div>
      </div>
    </div>
  )
}
