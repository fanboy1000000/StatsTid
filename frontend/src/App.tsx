import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { AuthProvider, useAuth } from './contexts/AuthContext'
import { ToastProvider } from './components/ui/Toast'
import { AppLayout } from './components/layout/AppLayout'
import { RequireAuth } from './components/guards/RequireAuth'
import { RequireRole } from './components/guards/RequireRole'
import { LoginPage } from './pages/LoginPage'
import { SkemaPage } from './pages/SkemaPage'
import { ArsoversigtPage } from './pages/ArsoversigtPage'
import { HealthDashboard } from './pages/HealthDashboard'
import { NotFoundPage } from './pages/NotFoundPage'
import { MyPeriods } from './pages/approval/MyPeriods'
// S87 / TASK-8702 (OQ-3): approvals moved to TeamOversigt at /godkend/oversigt.
// The old ApprovalDashboard was deleted in S88 (P2 parity reached); /godkend/godkendelser
// redirects here.
import { TeamOversigt } from './pages/approval/TeamOversigt'
import { OrganisationPage } from './pages/admin/OrganisationPage'
import { RoleManagement } from './pages/admin/RoleManagement'
import { ProjectManagement } from './pages/admin/ProjectManagement'
import { ConfigManagement } from './pages/config/ConfigManagement'
import { AgreementConfigList } from './pages/admin/AgreementConfigList'
import { AgreementConfigEditor } from './pages/admin/AgreementConfigEditor'
import { PositionOverrideManagement } from './pages/admin/PositionOverrideManagement'
import { WageTypeMappingManagement } from './pages/admin/WageTypeMappingManagement'
import { AuditLogView } from './pages/admin/AuditLogView'
import { MedarbejderAdministration } from './pages/admin/MedarbejderAdministration'
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
          <Route path="tid/oversigt" element={<ArsoversigtPage />} />
          <Route path="tid/mine-perioder" element={<MyPeriods />} />

          {/* === Godkend tid (LocalLeader+) === */}
          <Route element={<RequireRole minRole="LocalLeader" />}>
            <Route path="godkend/oversigt" element={<TeamOversigt />} />
            {/* S87 / TASK-8702 (OQ-3): approvals now live in the Teamoversigt; the old
                standalone dashboard route redirects (the component was deleted in S88). */}
            <Route path="godkend/godkendelser" element={<Navigate to="/godkend/oversigt" replace />} />
            <Route path="godkend/vikariering" element={<DelegationPage />} />
          </Route>

          {/* === Administration (mixed: LocalHR and LocalAdmin) === */}
          {/* LocalHR routes */}
          <Route element={<RequireRole minRole="LocalHR" />}>
            {/* S91 / TASK-9103: the old UserManagement list ("Medarbejdere") was
                removed; HR keeps employee management on the surviving tree page,
                opened to LocalHR here (a deliberate P7 expansion). */}
            <Route path="admin/ledelseslinjer" element={<MedarbejderAdministration />} />
            <Route path="admin/auditlog" element={<AuditLogView />} />
          </Route>
          {/* LocalAdmin routes within Administration */}
          <Route element={<RequireRole minRole="LocalAdmin" />}>
            <Route path="admin/projekter" element={<ProjectManagement />} />
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
            <Route path="global/organisation" element={<OrganisationPage />} />
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
