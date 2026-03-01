import { BrowserRouter, Routes, Route, Link } from 'react-router-dom'
import { TimeRegistration } from './pages/TimeRegistration'
import { WeeklyView } from './pages/WeeklyView'
import { AbsenceRegistration } from './pages/AbsenceRegistration'
import { HealthDashboard } from './pages/HealthDashboard'
import { LoginPage } from './pages/LoginPage'
import { useAuth } from './hooks/useAuth'

export function App() {
  const { isAuthenticated, user, login, logout } = useAuth()

  if (!isAuthenticated) {
    return <LoginPage onLogin={login} />
  }

  return (
    <BrowserRouter>
      <div style={{ fontFamily: 'system-ui, sans-serif', maxWidth: 1000, margin: '0 auto', padding: 20 }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <h1>StatsTid</h1>
          <div>
            <span style={{ marginRight: 12 }}>{user?.employeeId} ({user?.role})</span>
            <button onClick={logout}>Log ud</button>
          </div>
        </div>
        <nav style={{ marginBottom: 24, display: 'flex', gap: 16 }}>
          <Link to="/">Ugeoversigt</Link>
          <Link to="/time">Tidsregistrering</Link>
          <Link to="/absence">Fravaer</Link>
          <Link to="/health">Services</Link>
        </nav>
        <Routes>
          <Route path="/" element={<WeeklyView />} />
          <Route path="/time" element={<TimeRegistration />} />
          <Route path="/absence" element={<AbsenceRegistration />} />
          <Route path="/health" element={<HealthDashboard />} />
        </Routes>
      </div>
    </BrowserRouter>
  )
}
