import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// The API runs on http://localhost:5195 (src/HrPortal.Api launch profile "http").
// Proxying /api keeps the SPA and API same-origin in development, so no CORS is needed.
const apiUrl = process.env.HRPORTAL_API_URL ?? 'http://localhost:5195'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': apiUrl,
      '/health': apiUrl,
    },
  },
})
