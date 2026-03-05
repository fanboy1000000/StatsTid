import { useEffect, useState } from 'react'
import { Card, Table, Badge, Spinner } from '../components/ui'
import styles from './HealthDashboard.module.css'

interface ServiceHealth {
  name: string
  url: string
  status: string
}

const services = [
  { name: 'Backend API', url: 'http://localhost:5100/health' },
  { name: 'Rule Engine', url: 'http://localhost:5200/health' },
  { name: 'Orchestrator', url: 'http://localhost:5300/health' },
  { name: 'Payroll Integration', url: 'http://localhost:5400/health' },
  { name: 'External Integration', url: 'http://localhost:5500/health' },
  { name: 'Mock Payroll', url: 'http://localhost:5600/health' },
  { name: 'Mock External', url: 'http://localhost:5700/health' },
]

export function HealthDashboard() {
  const [healthChecks, setHealthChecks] = useState<ServiceHealth[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    async function checkHealth() {
      const results = await Promise.all(
        services.map(async (svc) => {
          try {
            const res = await fetch(svc.url)
            const data = await res.json()
            return { name: svc.name, url: svc.url, status: data.status ?? 'unknown' }
          } catch {
            return { name: svc.name, url: svc.url, status: 'unreachable' }
          }
        })
      )
      setHealthChecks(results)
      setLoading(false)
    }
    checkHealth()
  }, [])

  return (
    <div className={styles.page}>
      <h2 className={styles.title}>Service Health Dashboard</h2>
      <Card>
        {loading ? (
          <div className={styles.loadingWrapper}>
            <Spinner size="md" />
            <span>Checking services...</span>
          </div>
        ) : (
          <Table headers={['Service', 'Status']}>
            {healthChecks.map((svc) => (
              <tr key={svc.name}>
                <td>{svc.name}</td>
                <td>
                  <Badge variant={svc.status === 'healthy' ? 'success' : 'error'}>
                    {svc.status}
                  </Badge>
                </td>
              </tr>
            ))}
          </Table>
        )}
      </Card>
    </div>
  )
}
