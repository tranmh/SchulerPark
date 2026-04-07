import { Routes, Route } from 'react-router-dom'
import { ProtectedRoute } from './components/ProtectedRoute'
import { useAuth } from './contexts/AuthContext'
import { LoginPage } from './pages/Login/LoginPage'
import { RegisterPage } from './pages/Login/RegisterPage'
import './App.css'

function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/register" element={<RegisterPage />} />
      <Route path="/" element={
        <ProtectedRoute>
          <Dashboard />
        </ProtectedRoute>
      } />
    </Routes>
  )
}

function Dashboard() {
  const { user, logout } = useAuth()

  return (
    <div className="app">
      <header className="app-header">
        <h1>SchulerPark</h1>
        <div style={{ display: 'flex', alignItems: 'center', gap: 16 }}>
          <span>Welcome, {user?.displayName}</span>
          <button onClick={logout}>Logout</button>
        </div>
      </header>
      <main>
        <div className="home">
          <h2>Dashboard</h2>
          <p>Parkplatz-Buchungssystem fuer Schuler-Standorte.</p>
          <ul>
            <li>Goeppingen</li>
            <li>Erfurt</li>
            <li>Hessdorf</li>
            <li>Gemmingen</li>
          </ul>
        </div>
      </main>
    </div>
  )
}

export default App
