import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { AuthProvider, useAuth } from './contexts/AuthContext'
import { AppLayout } from './components/layout/AppLayout'
import { RequireAuth } from './components/guards/RequireAuth'
import { RequireRole } from './components/guards/RequireRole'
import { LoginPage } from './pages/LoginPage'
import { SkemaPage } from './pages/SkemaPage'
import { HealthDashboard } from './pages/HealthDashboard'
import { NotFoundPage } from './pages/NotFoundPage'
import { MyPeriods } from './pages/approval/MyPeriods'
import { ApprovalDashboard } from './pages/approval/ApprovalDashboard'
import { OrgManagement } from './pages/admin/OrgManagement'
import { UserManagement } from './pages/admin/UserManagement'
import { RoleManagement } from './pages/admin/RoleManagement'
import { ProjectManagement } from './pages/admin/ProjectManagement'
import { ConfigManagement } from './pages/config/ConfigManagement'
import './styles/tokens.css'

export function App() {
  return (
    <AuthProvider>
      <BrowserRouter>
        <AppRoutes />
      </BrowserRouter>
    </AuthProvider>
  )
}

function AppRoutes() {
  const { isAuthenticated } = useAuth()

  return (
    <Routes>
      <Route
        path="/login"
        element={
          isAuthenticated ? <Navigate to="/" replace /> : <LoginContent />
        }
      />

      {/* Protected routes */}
      <Route element={<RequireAuth />}>
        <Route element={<AppLayout />}>
          {/* Employee routes (all authenticated users) */}
          <Route index element={<SkemaPage />} />
          <Route path="health" element={<HealthDashboard />} />

          {/* Approval routes */}
          <Route path="approval/mine" element={<MyPeriods />} />

          {/* Leader routes */}
          <Route element={<RequireRole minRole="LocalLeader" />}>
            <Route path="approval" element={<ApprovalDashboard />} />
          </Route>

          {/* HR routes */}
          <Route element={<RequireRole minRole="LocalHR" />}>
            <Route path="admin/users" element={<UserManagement />} />
          </Route>

          {/* Admin routes */}
          <Route element={<RequireRole minRole="LocalAdmin" />}>
            <Route path="admin/orgs" element={<OrgManagement />} />
            <Route path="admin/roles" element={<RoleManagement />} />
            <Route path="admin/projects" element={<ProjectManagement />} />
            <Route path="config" element={<ConfigManagement />} />
          </Route>

          <Route path="*" element={<NotFoundPage />} />
        </Route>
      </Route>
    </Routes>
  )
}

function LoginContent() {
  const { login } = useAuth()
  return <LoginPage onLogin={login} />
}
