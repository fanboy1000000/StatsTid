import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { AuthProvider, useAuth } from './contexts/AuthContext'
import { ToastProvider } from './components/ui/Toast'
import { AppLayout } from './components/layout/AppLayout'
import { RequireAuth } from './components/guards/RequireAuth'
import { RequireRole } from './components/guards/RequireRole'
import { LoginPage } from './pages/LoginPage'
import { SkemaPage } from './pages/SkemaPage'
import { OversightPlaceholder } from './pages/OversightPlaceholder'
import { HealthDashboard } from './pages/HealthDashboard'
import { NotFoundPage } from './pages/NotFoundPage'
import { MyPeriods } from './pages/approval/MyPeriods'
import { ApprovalDashboard } from './pages/approval/ApprovalDashboard'
import { OrgManagement } from './pages/admin/OrgManagement'
import { UserManagement } from './pages/admin/UserManagement'
import { RoleManagement } from './pages/admin/RoleManagement'
import { ProjectManagement } from './pages/admin/ProjectManagement'
import { ConfigManagement } from './pages/config/ConfigManagement'
import { AgreementConfigList } from './pages/admin/AgreementConfigList'
import { AgreementConfigEditor } from './pages/admin/AgreementConfigEditor'
import { PositionOverrideManagement } from './pages/admin/PositionOverrideManagement'
import { WageTypeMappingManagement } from './pages/admin/WageTypeMappingManagement'
import { AuditLogView } from './pages/admin/AuditLogView'
import { ReportingLineTree } from './pages/admin/ReportingLineTree'
import { DelegationPage } from './pages/delegation/DelegationPage'
import './styles/tokens.css'

export function App() {
  return (
    <AuthProvider>
      <ToastProvider>
        <BrowserRouter>
          <AppRoutes />
        </BrowserRouter>
      </ToastProvider>
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
          isAuthenticated ? <Navigate to="/tid/registrering" replace /> : <LoginContent />
        }
      />

      {/* Protected routes */}
      <Route element={<RequireAuth />}>
        <Route element={<AppLayout />}>
          {/* Root redirect */}
          <Route index element={<Navigate to="/tid/registrering" replace />} />

          {/* === Min tid (Employee — all authenticated) === */}
          <Route path="tid/registrering" element={<SkemaPage />} />
          <Route path="tid/oversigt" element={<OversightPlaceholder />} />
          <Route path="tid/mine-perioder" element={<MyPeriods />} />

          {/* === Godkend tid (LocalLeader+) === */}
          <Route element={<RequireRole minRole="LocalLeader" />}>
            <Route path="godkend/godkendelser" element={<ApprovalDashboard />} />
            <Route path="godkend/vikariering" element={<DelegationPage />} />
          </Route>

          {/* === Administration (mixed: LocalHR and LocalAdmin) === */}
          {/* LocalHR routes */}
          <Route element={<RequireRole minRole="LocalHR" />}>
            <Route path="admin/medarbejdere" element={<UserManagement />} />
            <Route path="admin/auditlog" element={<AuditLogView />} />
          </Route>
          {/* LocalAdmin routes within Administration */}
          <Route element={<RequireRole minRole="LocalAdmin" />}>
            <Route path="admin/projekter" element={<ProjectManagement />} />
            <Route path="admin/ledelseslinjer" element={<ReportingLineTree />} />
            <Route path="admin/brugerrettigheder" element={<RoleManagement />} />
          </Route>

          {/* === Lokale tilpasninger (LocalAdmin+) === */}
          <Route element={<RequireRole minRole="LocalAdmin" />}>
            <Route path="lokal/ok-konfiguration" element={<ConfigManagement />} />
            <Route path="lokal/stillingstilpasninger" element={<PositionOverrideManagement />} />
          </Route>

          {/* === Global administration (GlobalAdmin) === */}
          <Route element={<RequireRole minRole="GlobalAdmin" />}>
            <Route path="global/overenskomster" element={<AgreementConfigList />} />
            <Route path="global/overenskomster/new" element={<AgreementConfigEditor />} />
            <Route path="global/overenskomster/:configId" element={<AgreementConfigEditor />} />
            <Route path="global/organisation" element={<OrgManagement />} />
            <Route path="global/loenartstilknytning" element={<WageTypeMappingManagement />} />
            <Route path="global/entitlement-configs" element={<Navigate to="/global/overenskomster" replace />} />
          </Route>

          {/* Health (Employee, hidden from nav) */}
          <Route path="health" element={<HealthDashboard />} />

          {/* Catch-all */}
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
