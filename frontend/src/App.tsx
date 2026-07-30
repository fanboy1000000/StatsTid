import { lazy } from 'react'
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { AuthProvider, useAuth } from './contexts/AuthContext'
import { ToastProvider } from './components/ui/Toast'
import { AppLayout } from './components/layout/AppLayout'
import { RequireAuth } from './components/guards/RequireAuth'
import { RequireRole } from './components/guards/RequireRole'
import { LoginPage } from './pages/LoginPage'
// S87 / TASK-8702 (OQ-3): approvals moved to TeamOversigt at /godkend/oversigt.
// The old ApprovalDashboard was deleted in S88 (P2 parity reached); /godkend/godkendelser
// redirects here.

// ── S125 / TASK-12504 (F3) — route-level code splitting ────────────────────────────────────
// Every page below was STATICALLY imported, so all 31 routes shipped in ONE 594 kB chunk: an
// employee who only ever opens Skema still downloaded the agreement-config editor, the wage-type
// mapping admin, the audit log and the whole org-management surface.
//
// These are named exports, so each lazy() maps the module's named binding onto `default` — the
// alternative (adding default exports) would change every page's public shape for a build concern.
//
// LoginPage stays EAGER above: it is the first paint for an unauthenticated visitor, and making it
// lazy would put a chunk request in front of the very first render — a worse first impression in
// exchange for bytes that user needs anyway.
//
// The Suspense boundary lives INSIDE AppLayout, around its <Outlet />, so the header, top nav and
// sidebar stay on screen while a page chunk loads and only the content region swaps. A boundary
// placed here instead would blank the whole window on every navigation.

const SkemaPage = lazy(() => import('./pages/SkemaPage').then(m => ({ default: m.SkemaPage })))
const ArsoversigtPage = lazy(() => import('./pages/ArsoversigtPage').then(m => ({ default: m.ArsoversigtPage })))
const HealthDashboard = lazy(() => import('./pages/HealthDashboard').then(m => ({ default: m.HealthDashboard })))
const NotFoundPage = lazy(() => import('./pages/NotFoundPage').then(m => ({ default: m.NotFoundPage })))
const MyPeriods = lazy(() => import('./pages/approval/MyPeriods').then(m => ({ default: m.MyPeriods })))
const TeamOversigt = lazy(() => import('./pages/approval/TeamOversigt').then(m => ({ default: m.TeamOversigt })))
const RoleManagement = lazy(() => import('./pages/admin/RoleManagement').then(m => ({ default: m.RoleManagement })))
const ProjectManagement = lazy(() => import('./pages/admin/ProjectManagement').then(m => ({ default: m.ProjectManagement })))
const ConfigManagement = lazy(() => import('./pages/config/ConfigManagement').then(m => ({ default: m.ConfigManagement })))
const AgreementConfigList = lazy(() => import('./pages/admin/AgreementConfigList').then(m => ({ default: m.AgreementConfigList })))
const AgreementConfigEditor = lazy(() => import('./pages/admin/AgreementConfigEditor').then(m => ({ default: m.AgreementConfigEditor })))
const PositionOverrideManagement = lazy(() => import('./pages/admin/PositionOverrideManagement').then(m => ({ default: m.PositionOverrideManagement })))
const WageTypeMappingManagement = lazy(() => import('./pages/admin/WageTypeMappingManagement').then(m => ({ default: m.WageTypeMappingManagement })))
const AuditLogView = lazy(() => import('./pages/admin/AuditLogView').then(m => ({ default: m.AuditLogView })))
const OrganisationOgMedarbejdere = lazy(() => import('./pages/admin/OrganisationOgMedarbejdere').then(m => ({ default: m.OrganisationOgMedarbejdere })))
const DelegationPage = lazy(() => import('./pages/delegation/DelegationPage').then(m => ({ default: m.DelegationPage })))

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
            {/* S109 / TASK-10904 (Enhedsspor cutover): the merged
                "Organisation & medarbejdere" page is now THE single admin surface.
                It is feature-complete (people editing + structure + settlement
                overview), so the two old pages are retired and their routes redirect
                here: admin/ledelseslinjer (the old "Medarbejder administration") and
                global/organisation (the old Global → Organisation). */}
            <Route path="admin/organisation-medarbejdere" element={<OrganisationOgMedarbejdere />} />
            <Route
              path="admin/ledelseslinjer"
              element={<Navigate to="/admin/organisation-medarbejdere" replace />}
            />
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
            {/* S109 / TASK-10904 (Enhedsspor cutover): the Global → Organisation page
                is retired; its route redirects to the merged admin surface. */}
            <Route
              path="global/organisation"
              element={<Navigate to="/admin/organisation-medarbejdere" replace />}
            />
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
