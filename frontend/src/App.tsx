import { Routes, Route } from 'react-router-dom'
import { ProtectedRoute } from './components/ProtectedRoute'
import { AppLayout } from './components/AppLayout'
import { LoginPage } from './pages/Login/LoginPage'
import { RegisterPage } from './pages/Login/RegisterPage'
import { DashboardPage } from './pages/Dashboard/DashboardPage'
import { BookingPage } from './pages/Booking/BookingPage'
import { MyBookingsPage } from './pages/MyBookings/MyBookingsPage'
import { LocationsPage } from './pages/Admin/LocationsPage'
import { SlotsPage } from './pages/Admin/SlotsPage'
import { BlockedDaysPage } from './pages/Admin/BlockedDaysPage'
import { BookingsPage } from './pages/Admin/BookingsPage'
import { LotteryHistoryPage } from './pages/Admin/LotteryHistoryPage'
import { GridLayoutPage } from './pages/Admin/GridLayoutPage'
import { UsersPage } from './pages/Admin/UsersPage'
import { ProfilePage } from './pages/Profile/ProfilePage'
import { PrivacyPage } from './pages/Privacy/PrivacyPage'

function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/register" element={<RegisterPage />} />
      <Route path="/" element={
        <ProtectedRoute>
          <AppLayout>
            <DashboardPage />
          </AppLayout>
        </ProtectedRoute>
      } />
      <Route path="/booking" element={
        <ProtectedRoute>
          <AppLayout>
            <BookingPage />
          </AppLayout>
        </ProtectedRoute>
      } />
      <Route path="/my-bookings" element={
        <ProtectedRoute>
          <AppLayout>
            <MyBookingsPage />
          </AppLayout>
        </ProtectedRoute>
      } />
      <Route path="/profile" element={
        <ProtectedRoute>
          <AppLayout>
            <ProfilePage />
          </AppLayout>
        </ProtectedRoute>
      } />
      <Route path="/privacy" element={<PrivacyPage />} />
      {/* Admin routes */}
      <Route path="/admin/locations" element={
        <ProtectedRoute requireAdmin>
          <AppLayout>
            <LocationsPage />
          </AppLayout>
        </ProtectedRoute>
      } />
      <Route path="/admin/slots" element={
        <ProtectedRoute requireAdmin>
          <AppLayout>
            <SlotsPage />
          </AppLayout>
        </ProtectedRoute>
      } />
      <Route path="/admin/blocked-days" element={
        <ProtectedRoute requireAdmin>
          <AppLayout>
            <BlockedDaysPage />
          </AppLayout>
        </ProtectedRoute>
      } />
      <Route path="/admin/bookings" element={
        <ProtectedRoute requireAdmin>
          <AppLayout>
            <BookingsPage />
          </AppLayout>
        </ProtectedRoute>
      } />
      <Route path="/admin/lottery-history" element={
        <ProtectedRoute requireAdmin>
          <AppLayout>
            <LotteryHistoryPage />
          </AppLayout>
        </ProtectedRoute>
      } />
      <Route path="/admin/grid-layout" element={
        <ProtectedRoute requireAdmin>
          <AppLayout>
            <GridLayoutPage />
          </AppLayout>
        </ProtectedRoute>
      } />
      <Route path="/admin/users" element={
        <ProtectedRoute requireSuperAdmin>
          <AppLayout>
            <UsersPage />
          </AppLayout>
        </ProtectedRoute>
      } />
    </Routes>
  )
}

export default App
