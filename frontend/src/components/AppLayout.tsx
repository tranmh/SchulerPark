import { NavLink } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';
import type { ReactNode } from 'react';

interface Props {
  children: ReactNode;
}

const navItems = [
  { to: '/', label: 'Dashboard', icon: DashboardIcon },
  { to: '/booking', label: 'Book a Spot', icon: BookingIcon },
  { to: '/my-bookings', label: 'My Bookings', icon: ListIcon },
];

export function AppLayout({ children }: Props) {
  const { user, logout, isAdmin } = useAuth();

  return (
    <div className="flex h-screen bg-gray-50">
      {/* Sidebar */}
      <aside className="flex w-64 flex-col bg-gray-900 text-white">
        {/* Brand */}
        <div className="flex h-16 items-center px-6">
          <span className="text-xl font-bold tracking-tight">SchulerPark</span>
        </div>

        {/* Navigation */}
        <nav className="mt-2 flex-1 space-y-1 px-3">
          {navItems.map(({ to, label, icon: Icon }) => (
            <NavLink
              key={to}
              to={to}
              end={to === '/'}
              className={({ isActive }) =>
                `flex items-center gap-3 rounded-lg px-3 py-2.5 text-sm font-medium transition-colors ${
                  isActive
                    ? 'bg-gray-800 text-white'
                    : 'text-gray-400 hover:bg-gray-800 hover:text-white'
                }`
              }
            >
              <Icon />
              {label}
            </NavLink>
          ))}
        </nav>

        {/* User section */}
        <div className="border-t border-gray-800 p-4">
          <div className="mb-2">
            <div className="text-sm font-medium">{user?.displayName}</div>
            <div className="text-xs text-gray-400">{user?.email}</div>
            {isAdmin && (
              <span className="mt-1 inline-block rounded bg-amber-600 px-1.5 py-0.5 text-xs font-medium">
                Admin
              </span>
            )}
          </div>
          <button
            onClick={logout}
            className="w-full rounded-md bg-gray-800 px-3 py-1.5 text-sm text-gray-300 hover:bg-gray-700 hover:text-white"
          >
            Sign out
          </button>
        </div>
      </aside>

      {/* Main content */}
      <main className="flex-1 overflow-auto p-8">
        {children}
      </main>
    </div>
  );
}

function DashboardIcon() {
  return (
    <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5}
        d="M3 12l2-2m0 0l7-7 7 7M5 10v10a1 1 0 001 1h3m10-11l2 2m-2-2v10a1 1 0 01-1 1h-3m-4 0a1 1 0 01-1-1v-4a1 1 0 011-1h2a1 1 0 011 1v4a1 1 0 01-1 1h-2z" />
    </svg>
  );
}

function BookingIcon() {
  return (
    <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5}
        d="M12 4v16m8-8H4" />
    </svg>
  );
}

function ListIcon() {
  return (
    <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5}
        d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2" />
    </svg>
  );
}
