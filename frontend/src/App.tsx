import { Route, Routes } from 'react-router-dom'
import { RequireAuth } from './auth/RequireAuth'
import { NAVIGATION } from './auth/navigation'
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
import { ConnectionStepper } from './features/connection/ConnectionStepper'
import InstallationListPage from './features/installations/InstallationListPage'
import InstallationDetailPage from './features/installations/InstallationDetailPage'
import Dashboard from './pages/Dashboard'
import { ForbiddenPage, LoginPage, NotFoundPage, SectionPage } from './pages/pages'

const WIRED = ['/', '/customers', '/customers/new', '/catalog', '/quotes', '/contracts', '/installations', '/admin/users', '/designs']

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route element={<RequireAuth />}>
        <Route element={<AppShell />}>
          <Route index element={<Dashboard />} />
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
          <Route path="/connection-requests/:id" element={<ConnectionRequestPage />} />
          <Route path="/installations" element={<InstallationListPage />} />
          <Route
            path="/installations/:id"
            element={<InstallationDetailPage renderConnection={(i) => <ConnectionStepper installationId={i.id} />} />}
          />
          <Route path="/forbidden" element={<ForbiddenPage />} />
          <Route path="*" element={<NotFoundPage />} />
        </Route>
      </Route>
    </Routes>
  )
}
