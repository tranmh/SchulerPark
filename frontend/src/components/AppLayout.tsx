import { useEffect, useRef, useState } from 'react';
import { NavLink, useLocation } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../contexts/AuthContext';
import { useBodyScrollLock } from '../hooks/useBodyScrollLock';
import { useMediaQuery } from '../hooks/useMediaQuery';
import { LanguageToggle } from './LanguageToggle';
import type { ReactNode } from 'react';

/** Matches Tailwind's `lg` breakpoint: the sidebar is static from here up. */
const DESKTOP_QUERY = '(min-width: 64rem)';

interface Props {
  children: ReactNode;
}

function navLinkClass({ isActive }: { isActive: boolean }) {
  return [
    'group relative flex min-h-11 items-center gap-3 rounded-lg px-3.5 py-2.5 text-sm font-medium transition-colors',
    isActive
      ? 'text-white bg-gradient-to-r from-brand-500/25 to-brand-500/[0.04]'
      : 'text-ink-300 hover:text-white hover:bg-white/[0.04]',
  ].join(' ');
}

export function AppLayout({ children }: Props) {
  const { t } = useTranslation();
  const { user, logout, isAdmin, isSuperAdmin } = useAuth();
  const location = useLocation();
  const isDesktop = useMediaQuery(DESKTOP_QUERY);

  // Mobile drawer state. On desktop (lg+) the aside is always visible and
  // `open` is irrelevant; the hamburger that toggles it is hidden there.
  const [open, setOpen] = useState(false);
  const closeMenu = () => setOpen(false);
  const menuButtonRef = useRef<HTMLButtonElement>(null);
  const firstLinkRef = useRef<HTMLAnchorElement>(null);
  const wasOpen = useRef(false);

  // Close when the route changes (link tap, redirect, back button).
  useEffect(() => {
    setOpen(false);
  }, [location.pathname]);

  // Escape closes the drawer.
  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open]);

  useBodyScrollLock(open && !isDesktop);

  // Move focus into the drawer on open and back to the trigger on close.
  useEffect(() => {
    if (open) {
      firstLinkRef.current?.focus();
    } else if (wasOpen.current) {
      menuButtonRef.current?.focus();
    }
    wasOpen.current = open;
  }, [open]);

  // The drawer is off-canvas but still in the DOM below lg: make it inert so
  // it can't be tabbed into or read by screen readers while closed.
  const drawerInert = !open && !isDesktop;

  const navItems = [
    { to: '/', label: t('nav.dashboard'), icon: DashboardIcon },
    { to: '/booking', label: t('nav.bookSpot'), icon: BookingIcon },
    { to: '/my-bookings', label: t('nav.myBookings'), icon: ListIcon },
  ];

  const adminNavItems = [
    { to: '/admin/locations', label: t('nav.locations'), icon: LocationIcon },
    { to: '/admin/slots', label: t('nav.parkingSlots'), icon: SlotIcon },
    { to: '/admin/grid-layout', label: t('nav.gridLayout'), icon: GridIcon },
    { to: '/admin/blocked-days', label: t('nav.blockedDays'), icon: BlockIcon },
    { to: '/admin/bookings', label: t('nav.allBookings'), icon: ListIcon },
    { to: '/admin/lottery-history', label: t('nav.lotteryHistory'), icon: HistoryIcon },
    { to: '/admin/approvals', label: t('nav.approvals'), icon: UsersIcon },
  ];

  const superAdminNavItems = [
    { to: '/admin/users', label: t('nav.users'), icon: UsersIcon },
  ];

  const initials = (user?.displayName ?? '')
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((p) => p[0]?.toUpperCase() ?? '')
    .join('') || 'U';

  const brand = (
    <div className="flex items-center gap-2.5">
      <span className="grid h-8 w-8 place-items-center rounded-lg bg-gradient-to-br from-brand-400 to-brand-700 text-[13px] font-extrabold tracking-tight text-white shadow-[inset_0_1px_0_rgba(255,255,255,0.15)]">
        LE
      </span>
      <div className="leading-tight">
        <div className="text-[15px] font-semibold tracking-tight">LouisE</div>
        <div className="text-[10.5px] text-ink-400">{t('nav.tagline')}</div>
      </div>
    </div>
  );

  return (
    <div className="flex h-dvh flex-col bg-surface-sunken lg:flex-row">
      {/* Mobile top bar (hidden from lg up, where the sidebar is static) */}
      <header className="flex min-h-14 shrink-0 items-center gap-2 bg-ink-900 pl-safe pr-safe pt-safe text-white lg:hidden">
        <button
          ref={menuButtonRef}
          type="button"
          onClick={() => setOpen(true)}
          aria-label={t('nav.openMenu')}
          aria-expanded={open}
          aria-controls="app-sidebar"
          className="ml-1 grid h-11 w-11 shrink-0 place-items-center rounded-lg text-ink-200 transition-colors hover:bg-white/[0.06] hover:text-white"
        >
          <svg className="h-6 w-6" fill="none" viewBox="0 0 24 24" stroke="currentColor" aria-hidden="true">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.75} d="M4 6h16M4 12h16M4 18h16" />
          </svg>
        </button>
        {brand}
        <LanguageToggle variant="dark" className="ml-auto mr-3" />
      </header>

      {/* Backdrop behind the open drawer */}
      {open && (
        <div
          className="fixed inset-0 z-40 bg-ink-900/55 backdrop-blur-[2px] lg:hidden"
          onClick={closeMenu}
          aria-hidden="true"
        />
      )}

      {/* Sidebar: off-canvas drawer below lg, static column from lg up */}
      <aside
        id="app-sidebar"
        inert={drawerInert}
        className={[
          'fixed inset-y-0 left-0 z-50 flex w-64 max-w-[85vw] shrink-0 flex-col bg-ink-900 text-white',
          'transition-transform duration-200 ease-out',
          'lg:static lg:max-w-none lg:translate-x-0 lg:transition-none',
          open ? 'translate-x-0' : '-translate-x-full',
        ].join(' ')}
      >
        {/* Brand */}
        <div className="flex min-h-16 items-center justify-between px-5 pt-safe">
          {brand}
          <div className="hidden lg:block">
            <LanguageToggle variant="dark" />
          </div>
          <button
            type="button"
            onClick={closeMenu}
            aria-label={t('nav.closeMenu')}
            className="-mr-2 grid h-11 w-11 place-items-center rounded-lg text-ink-300 transition-colors hover:bg-white/[0.06] hover:text-white lg:hidden"
          >
            <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" aria-hidden="true">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.75} d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>

        {/* Navigation */}
        <nav className="mt-1 flex-1 space-y-0.5 px-2.5 overflow-y-auto scroll-thin">
          {navItems.map(({ to, label, icon: Icon }, index) => (
            <NavLink
              key={to}
              to={to}
              end={to === '/'}
              className={navLinkClass}
              onClick={closeMenu}
              ref={index === 0 ? firstLinkRef : undefined}
            >
              {({ isActive }) => (
                <>
                  {isActive && (
                    <span className="absolute left-[-6px] top-2 bottom-2 w-[3px] rounded-full bg-brand-300" />
                  )}
                  <Icon />
                  <span>{label}</span>
                </>
              )}
            </NavLink>
          ))}

          {isAdmin && (
            <>
              <div className="mt-5 mb-1.5 px-3.5 text-[10.5px] font-semibold uppercase tracking-[0.08em] text-ink-500">
                {t('nav.admin')}
              </div>
              {adminNavItems.map(({ to, label, icon: Icon }) => (
                <NavLink key={to} to={to} className={navLinkClass} onClick={closeMenu}>
                  {({ isActive }) => (
                    <>
                      {isActive && (
                        <span className="absolute left-[-6px] top-2 bottom-2 w-[3px] rounded-full bg-brand-300" />
                      )}
                      <Icon />
                      <span>{label}</span>
                    </>
                  )}
                </NavLink>
              ))}
            </>
          )}

          {isSuperAdmin && (
            <>
              <div className="mt-5 mb-1.5 px-3.5 text-[10.5px] font-semibold uppercase tracking-[0.08em] text-ink-500">
                {t('nav.superAdmin')}
              </div>
              {superAdminNavItems.map(({ to, label, icon: Icon }) => (
                <NavLink key={to} to={to} className={navLinkClass} onClick={closeMenu}>
                  {({ isActive }) => (
                    <>
                      {isActive && (
                        <span className="absolute left-[-6px] top-2 bottom-2 w-[3px] rounded-full bg-brand-300" />
                      )}
                      <Icon />
                      <span>{label}</span>
                    </>
                  )}
                </NavLink>
              ))}
            </>
          )}
        </nav>

        {/* User section */}
        <div className="border-t border-white/[0.07] p-3 pb-[calc(0.75rem+env(safe-area-inset-bottom))]">
          <div className="flex items-center gap-3 px-2 py-2">
            <div className="grid h-9 w-9 shrink-0 place-items-center rounded-full bg-gradient-to-br from-brand-300 to-brand-700 text-[13px] font-semibold text-white">
              {initials}
            </div>
            <div className="min-w-0 flex-1">
              <div className="truncate text-[13px] font-medium text-white">{user?.displayName}</div>
              <div className="truncate text-[11px] text-ink-400">{user?.email}</div>
            </div>
            {isSuperAdmin ? (
              <span className="rounded px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wider text-violet-200 bg-violet-500/15 ring-1 ring-violet-400/30">
                {t('nav.roleSuper')}
              </span>
            ) : isAdmin ? (
              <span className="rounded px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wider text-amber-200 bg-amber-400/10 ring-1 ring-amber-300/30">
                {t('nav.roleAdmin')}
              </span>
            ) : null}
          </div>
          <div className="mt-1 grid grid-cols-2 gap-1.5 px-2">
            <NavLink
              to="/profile"
              onClick={closeMenu}
              className={({ isActive }) =>
                `rounded-md py-2.5 text-center text-[12px] font-medium transition-colors ${
                  isActive
                    ? 'bg-white/10 text-white'
                    : 'bg-white/[0.04] text-ink-200 hover:bg-white/10 hover:text-white'
                }`
              }
            >
              {t('nav.profile')}
            </NavLink>
            <button
              type="button"
              onClick={logout}
              className="rounded-md bg-white/[0.04] py-2.5 text-[12px] font-medium text-ink-200 transition-colors hover:bg-white/10 hover:text-white"
            >
              {t('nav.signOut')}
            </button>
          </div>
        </div>
      </aside>

      {/* Main content */}
      <main className="min-h-0 min-w-0 flex-1 overflow-auto scroll-thin">
        <div className="mx-auto max-w-[1400px] px-4 py-5 sm:px-6 sm:py-7 lg:px-10 lg:py-9">{children}</div>
      </main>
    </div>
  );
}

/* ------------------------------------------------------------------ */
/*  Icons (1.5px stroke, 18px nominal)                                  */
/* ------------------------------------------------------------------ */
function I(props: { d: string; extra?: string }) {
  return (
    <svg className="h-[18px] w-[18px] shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d={props.d} />
      {props.extra && (
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d={props.extra} />
      )}
    </svg>
  );
}

function DashboardIcon() {
  return <I d="M3 12l2-2m0 0l7-7 7 7M5 10v10a1 1 0 001 1h3m10-11l2 2m-2-2v10a1 1 0 01-1 1h-3m-4 0a1 1 0 01-1-1v-4a1 1 0 011-1h2a1 1 0 011 1v4a1 1 0 01-1 1h-2z" />;
}
function BookingIcon() {
  return <I d="M12 4v16m8-8H4" />;
}
function ListIcon() {
  return <I d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2" />;
}
function LocationIcon() {
  return <I d="M17.657 16.657L13.414 20.9a1.998 1.998 0 01-2.827 0l-4.244-4.243a8 8 0 1111.314 0z" extra="M15 11a3 3 0 11-6 0 3 3 0 016 0z" />;
}
function SlotIcon() {
  return <I d="M19 21V5a2 2 0 00-2-2H7a2 2 0 00-2 2v16m14 0h2m-2 0h-5m-9 0H3m2 0h5M9 7h1m-1 4h1m4-4h1m-1 4h1m-5 10v-5a1 1 0 011-1h2a1 1 0 011 1v5m-4 0h4" />;
}
function BlockIcon() {
  return <I d="M18.364 18.364A9 9 0 005.636 5.636m12.728 12.728A9 9 0 015.636 5.636m12.728 12.728L5.636 5.636" />;
}
function GridIcon() {
  return <I d="M3 3h7v7H3V3zm11 0h7v7h-7V3zm0 11h7v7h-7v-7zM3 14h7v7H3v-7z" />;
}
function HistoryIcon() {
  return <I d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z" />;
}
function UsersIcon() {
  return <I d="M17 20h5v-2a4 4 0 00-3-3.87M9 20H4v-2a4 4 0 013-3.87m6-2a4 4 0 11-8 0 4 4 0 018 0zm6-3a3 3 0 11-6 0 3 3 0 016 0z" />;
}
