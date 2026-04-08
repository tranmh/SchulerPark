import { Routes, Route } from 'react-router-dom'
import { ProtectedRoute } from './components/ProtectedRoute'
import { AppLayout } from './components/AppLayout'
import { LoginPage } from './pages/Login/LoginPage'
import { RegisterPage } from './pages/Login/RegisterPage'
import { DashboardPage } from './pages/Dashboard/DashboardPage'
import { BookingPage } from './pages/Booking/BookingPage'
import { MyBookingsPage } from './pages/MyBookings/MyBookingsPage'

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
    </Routes>
  )
}

export default App
