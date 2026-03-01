import { useEffect, useState } from 'react'

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
    <div>
      <h2>Service Health Dashboard</h2>
      {loading ? (
        <p>Checking services...</p>
      ) : (
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr>
              <th style={{ textAlign: 'left', padding: 8, borderBottom: '2px solid #333' }}>Service</th>
              <th style={{ textAlign: 'left', padding: 8, borderBottom: '2px solid #333' }}>Status</th>
            </tr>
          </thead>
          <tbody>
            {healthChecks.map((svc) => (
              <tr key={svc.name}>
                <td style={{ padding: 8, borderBottom: '1px solid #ccc' }}>{svc.name}</td>
                <td style={{
                  padding: 8,
                  borderBottom: '1px solid #ccc',
                  color: svc.status === 'healthy' ? 'green' : 'red'
                }}>
                  {svc.status}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
