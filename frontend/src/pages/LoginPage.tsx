import { useState, type FormEvent } from 'react'
import { Card, FormField, Input, Button, Alert } from '../components/ui'
import styles from './LoginPage.module.css'

interface Props {
  onLogin: (username: string, password: string) => Promise<void>
}

/** A seeded login used only for manual dev testing (behind import.meta.env.DEV). */
interface TestPersona {
  /** Danish role label shown to the tester. */
  role: string
  /** Seed username the click-to-fill button writes into the form. */
  username: string
  /** Which features this persona is meant to exercise. */
  tests: string
  /** Baseline seed user (not part of the rich demo world). */
  baseline?: boolean
}

export function LoginPage({ onLogin }: Props) {
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)

  // Dev-only test personas — verified against the demo seed. Ordered low → high role.
  const testPersonas: TestPersona[] = [
    {
      role: 'Medarbejder',
      username: 'demo_styx1_0284',
      tests: 'Skema/tidsregistrering, Årsoversigt, Mine perioder',
    },
    {
      role: 'Leder',
      username: 'demo_styx1_0002',
      tests: 'Godkend tid (Team-/Leder-oversigt), Vikariering',
    },
    {
      role: 'HR',
      username: 'demo_styx1_0001',
      tests: 'Organisation & medarbejdere, Audit log',
    },
    {
      role: 'Lokal admin',
      username: 'ladm01',
      tests: 'Projekter, Brugerrettigheder, Lokal OK-konfiguration',
      baseline: true,
    },
    {
      role: 'Global admin',
      username: 'demo_admin',
      tests: 'Overenskomster, Lønartstilknytning',
    },
  ]

  // Click-to-fill: populate the visible fields; the tester still presses "Log ind".
  const fillLogin = (persona: TestPersona) => {
    setUsername(persona.username)
    setPassword('password')
  }

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
        {import.meta.env.DEV && (
          <div className={styles.personas}>
            <p className={styles.personasHeading}>Test-personaer</p>
            <p className={styles.personasIntro}>
              Adgangskode for alle: <code className={styles.code}>password</code>
            </p>
            <ul className={styles.personaList}>
              {testPersonas.map(persona => (
                <li key={persona.username}>
                  <button
                    type="button"
                    className={styles.persona}
                    onClick={() => fillLogin(persona)}
                    aria-label={`Udfyld login som ${persona.role} (${persona.username})`}
                  >
                    <span className={styles.personaHead}>
                      <span className={styles.personaRole}>
                        {persona.role}
                        {persona.baseline && (
                          <span className={styles.personaBaseline}> (baseline)</span>
                        )}
                      </span>
                      <code className={styles.personaUser}>{persona.username}</code>
                    </span>
                    <span className={styles.personaTests}>{persona.tests}</span>
                  </button>
                </li>
              ))}
            </ul>
            <p className={styles.personasNote}>
              Højere roller ser også alle lavere faner.
            </p>
          </div>
        )}
      </div>
    </div>
  )
}
