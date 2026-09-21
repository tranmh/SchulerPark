import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { VitePWA } from 'vite-plugin-pwa'

export default defineConfig(({ mode }) => ({
  plugins: [
    react(),
    tailwindcss(),
    VitePWA({
      strategies: 'injectManifest',
      srcDir: 'src',
      filename: 'sw.ts',
      registerType: 'autoUpdate',
      // Icons are generated from scripts/generate-icons.py — edit the script, not the PNGs.
      includeAssets: ['favicon.ico', 'icon.svg', 'apple-touch-icon-180x180.png', 'badge-96x96.png'],
      manifest: {
        name: 'LouisE - Parkplatz Buchungssystem',
        short_name: 'LouisE',
        description: 'Parking slot booking system for Schuler office locations',
        lang: 'de',
        // Matches the sidebar (--color-ink-900) and <meta name="theme-color"> in index.html.
        theme_color: '#0B0F17',
        // Matches the app background (--color-surface-sunken) so the splash screen blends in.
        background_color: '#F6F7F9',
        display: 'standalone',
        scope: '/',
        start_url: '/',
        icons: [
          {
            src: 'pwa-192x192.png',
            sizes: '192x192',
            type: 'image/png',
            purpose: 'any',
          },
          {
            src: 'pwa-512x512.png',
            sizes: '512x512',
            type: 'image/png',
            purpose: 'any',
          },
          {
            // Full-bleed variant with the mark inside the 80% safe zone — Android
            // masks this to its own launcher shape instead of cropping the 'any' icon.
            src: 'pwa-maskable-512x512.png',
            sizes: '512x512',
            type: 'image/png',
            purpose: 'maskable',
          },
        ],
      },
      injectManifest: {
        globPatterns: ['**/*.{js,css,html,ico,png,svg,woff2}'],
      },
    }),
  ],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
    },
    // WSL2 + /mnt/c/ does not deliver inotify events reliably; poll for HMR.
    watch: {
      usePolling: true,
      interval: 500,
    },
  },
  build: {
    outDir: 'dist',
    // No source maps in production builds — they expose the full TS source.
    sourcemap: mode !== 'production',
  },
}))
