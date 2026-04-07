import { Routes, Route } from 'react-router-dom'
import './App.css'

function App() {
  return (
    <div className="app">
      <header className="app-header">
        <h1>SchulerPark</h1>
        <p>Parkplatz Buchungssystem</p>
      </header>
      <main>
        <Routes>
          <Route path="/" element={<Home />} />
        </Routes>
      </main>
    </div>
  )
}

function Home() {
  return (
    <div className="home">
      <h2>Willkommen bei SchulerPark</h2>
      <p>Das Parkplatz-Buchungssystem fuer Schuler-Standorte.</p>
      <ul>
        <li>Goeppingen</li>
        <li>Erfurt</li>
        <li>Hessdorf</li>
        <li>Gemmingen</li>
      </ul>
    </div>
  )
}

export default App
