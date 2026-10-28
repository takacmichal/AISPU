import { Route, Routes, useParams } from 'react-router-dom'
import { RequireAuth } from './auth/RequireAuth'
import { NAVIGATION } from './auth/navigation'
import { AppShell } from './layout/AppShell'
import CustomerFormPage from './features/customers/CustomerFormPage'
import CustomerDetailPage from './features/customers/CustomerDetailPage'
import CustomerListPage from './features/customers/CustomerListPage'
import Dashboard from './pages/Dashboard'
import { ForbiddenPage, LoginPage, NotFoundPage, SectionPage } from './pages/pages'

function DetailPlaceholder({ section }: { section: string }) {
  const { id } = useParams()
  return (
    <section>
      <h2>{section}</h2>
      <p>Detail {id} sa pripravuje.</p>
    </section>
  )
}

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route element={<RequireAuth />}>
        <Route element={<AppShell />}>
          <Route index element={<Dashboard />} />
          {NAVIGATION.filter((item) => item.path !== '/' && item.path !== '/customers' && item.path !== '/customers/new').map((item) => (
            <Route key={item.path} path={item.path} element={<SectionPage path={item.path} />} />
          ))}
          <Route path="/customers" element={<CustomerListPage />} />
          <Route path="/customers/new" element={<CustomerFormPage />} />
          <Route path="/customers/:id" element={<CustomerDetailPage />} />
          <Route path="/customers/:id/edit" element={<CustomerFormPage />} />
          <Route path="/quotes/:id" element={<DetailPlaceholder section="Ponuka" />} />
          <Route path="/contracts/:id" element={<DetailPlaceholder section="Zmluva" />} />
          <Route path="/installations/:id" element={<DetailPlaceholder section="Inštalácia" />} />
          <Route path="/forbidden" element={<ForbiddenPage />} />
          <Route path="*" element={<NotFoundPage />} />
        </Route>
      </Route>
    </Routes>
  )
}
