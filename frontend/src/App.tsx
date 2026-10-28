import { Route, Routes } from 'react-router-dom'
import { RequireAuth } from './auth/RequireAuth'
import { NAVIGATION } from './auth/navigation'
import { AppShell } from './layout/AppShell'
import Dashboard from './pages/Dashboard'
import { ForbiddenPage, LoginPage, NotFoundPage, SectionPage } from './pages/pages'

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route element={<RequireAuth />}>
        <Route element={<AppShell />}>
          <Route index element={<Dashboard />} />
          {NAVIGATION.filter((item) => item.path !== '/').map((item) => (
            <Route key={item.path} path={item.path} element={<SectionPage path={item.path} />} />
          ))}
          <Route path="/forbidden" element={<ForbiddenPage />} />
          <Route path="*" element={<NotFoundPage />} />
        </Route>
      </Route>
    </Routes>
  )
}
