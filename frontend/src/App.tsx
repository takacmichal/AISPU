import { lazy, Suspense } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import { useAuth } from './auth/AuthContext'
import { RequireAuth } from './auth/RequireAuth'
import { HOME_PATH, NAVIGATION } from './auth/navigation'
import { AppShell } from './layout/AppShell'
import CustomerFormPage from './features/customers/CustomerFormPage'
import CustomerDetailPage from './features/customers/CustomerDetailPage'
import CustomerListPage from './features/customers/CustomerListPage'
import CatalogPage from './features/catalog/CatalogPage'
import UsersAdminPage from './features/admin/UsersAdminPage'
import QuoteListPage from './features/quotes/QuoteListPage'
import QuoteDetailPage from './features/quotes/QuoteDetailPage'
import DesignListPage from './features/designs/DesignListPage'
import DesignDetailPage from './features/designs/DesignDetailPage'
import ContractListPage from './features/contracts/ContractListPage'
import ContractDetailPage from './features/contracts/ContractDetailPage'
import ConnectionRequestPage from './features/connection/ConnectionRequestPage'
import ConnectionRequestListPage from './features/connection/ConnectionRequestListPage'
import { ConnectionStepper } from './features/connection/ConnectionStepper'
import InstallationListPage from './features/installations/InstallationListPage'
import InstallationDetailPage from './features/installations/InstallationDetailPage'
import MyPlantsPage from './features/portal/MyPlantsPage'
import MyPlantDetailPage from './features/portal/MyPlantDetailPage'
import TicketListPage from './features/tickets/TicketListPage'
import TicketDetailPage from './features/tickets/TicketDetailPage'
import TicketCreatePage from './features/tickets/TicketCreatePage'
import MonitoringPage from './features/monitoring/MonitoringPage'
import RegisterPage from './features/auth/RegisterPage'
import ForgotPasswordPage from './features/auth/ForgotPasswordPage'
import ResetPasswordPage from './features/auth/ResetPasswordPage'
import Dashboard from './pages/Dashboard'
import { ForbiddenPage, LoginPage, NotFoundPage, SectionPage } from './pages/pages'

// The public landing page is a separate chunk: signed-in users never download it.
const LandingPage = lazy(() => import('./features/landing/LandingPage'))

/** "/" is the landing page for visitors; signed-in users go straight to the app. */
function PublicHome() {
  const { session } = useAuth()
  if (session) return <Navigate to={HOME_PATH} replace />
  return (
    <Suspense fallback={null}>
      <LandingPage />
    </Suspense>
  )
}

// Dev-only component overview; import.meta.env.DEV is false in production builds, so the chunk is dropped there.
const UiShowcase = import.meta.env.DEV ? lazy(() => import('./dev/UiShowcase')) : null

const WIRED = [HOME_PATH, '/customers', '/customers/new', '/catalog', '/quotes', '/contracts', '/installations', '/admin/users', '/designs', '/my-plants', '/tickets', '/tickets/new', '/monitoring', '/connection-requests']

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/register" element={<RegisterPage />} />
      <Route path="/forgot-password" element={<ForgotPasswordPage />} />
      <Route path="/reset-password" element={<ResetPasswordPage />} />
      <Route path="/" element={<PublicHome />} />
      <Route element={<RequireAuth />}>
        <Route element={<AppShell />}>
          <Route path={HOME_PATH} element={<Dashboard />} />
          {NAVIGATION.filter((item) => !WIRED.includes(item.path)).map((item) => (
            <Route key={item.path} path={item.path} element={<SectionPage path={item.path} />} />
          ))}
          <Route path="/customers" element={<CustomerListPage />} />
          <Route path="/customers/new" element={<CustomerFormPage />} />
          <Route path="/customers/:id" element={<CustomerDetailPage />} />
          <Route path="/customers/:id/edit" element={<CustomerFormPage />} />
          <Route path="/catalog" element={<CatalogPage />} />
          <Route path="/admin/users" element={<UsersAdminPage />} />
          <Route path="/quotes" element={<QuoteListPage />} />
          <Route path="/quotes/:id" element={<QuoteDetailPage />} />
          <Route path="/designs" element={<DesignListPage />} />
          <Route path="/designs/:id" element={<DesignDetailPage />} />
          <Route path="/contracts" element={<ContractListPage />} />
          <Route path="/contracts/:id" element={<ContractDetailPage />} />
          <Route path="/connection-requests" element={<ConnectionRequestListPage />} />
          <Route path="/connection-requests/:id" element={<ConnectionRequestPage />} />
          <Route path="/installations" element={<InstallationListPage />} />
          <Route
            path="/installations/:id"
            element={<InstallationDetailPage renderConnection={(i) => <ConnectionStepper installationId={i.id} />} />}
          />
          <Route path="/my-plants" element={<MyPlantsPage />} />
          <Route path="/my-plants/:id" element={<MyPlantDetailPage />} />
          <Route path="/tickets" element={<TicketListPage />} />
          <Route path="/tickets/new" element={<TicketCreatePage />} />
          <Route path="/tickets/:id" element={<TicketDetailPage />} />
          <Route path="/monitoring" element={<MonitoringPage />} />
          {UiShowcase && (
            <Route
              path="/dev/ui"
              element={
                <Suspense fallback={null}>
                  <UiShowcase />
                </Suspense>
              }
            />
          )}
          <Route path="/forbidden" element={<ForbiddenPage />} />
          <Route path="*" element={<NotFoundPage />} />
        </Route>
      </Route>
    </Routes>
  )
}
