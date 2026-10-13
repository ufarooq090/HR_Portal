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
    // Pre-transform the app at startup so the first page load (e.g. in E2E runs) does not wait on compilation.
    warmup: { clientFiles: ['./src/main.tsx', './src/app/router.tsx'] },
    proxy: {
      '/api': apiUrl,
      '/health': apiUrl,
    },
  },
})
