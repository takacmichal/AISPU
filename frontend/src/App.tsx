import { Link, Route, Routes, useParams } from 'react-router-dom'
import { RequireAuth } from './auth/RequireAuth'
import { NAVIGATION } from './auth/navigation'
import { AppShell } from './layout/AppShell'
import CustomerFormPage from './features/customers/CustomerFormPage'
import CustomerListPage from './features/customers/CustomerListPage'
import Dashboard from './pages/Dashboard'
import { ForbiddenPage, LoginPage, NotFoundPage, SectionPage } from './pages/pages'

function CustomerDetailPlaceholder() {
  const { id } = useParams()
  return (
    <section>
      <h2>Detail zákazníka</h2>
      <Link to={`/customers/${id}/edit`}>Upraviť</Link>
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
          <Route path="/customers/:id" element={<CustomerDetailPlaceholder />} />
          <Route path="/customers/:id/edit" element={<CustomerFormPage />} />
          <Route path="/forbidden" element={<ForbiddenPage />} />
          <Route path="*" element={<NotFoundPage />} />
        </Route>
      </Route>
    </Routes>
  )
}
