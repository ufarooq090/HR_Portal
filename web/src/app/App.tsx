import { useSyncExternalStore } from 'react'
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { RouterProvider } from 'react-router'
import { ApiError } from '../api/client'
import { AuthProvider } from '../auth/AuthProvider'
import { router } from './router'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      // Retrying auth and permission failures never helps.
      retry: (failureCount, error) =>
        !(error instanceof ApiError && error.status >= 400 && error.status < 500) && failureCount < 2,
    },
  },
})

const darkQuery = window.matchMedia('(prefers-color-scheme: dark)')

function usePrefersDark(): boolean {
  return useSyncExternalStore(
    (onChange) => {
      darkQuery.addEventListener('change', onChange)
      return () => darkQuery.removeEventListener('change', onChange)
    },
    () => darkQuery.matches,
  )
}

export function App() {
  const prefersDark = usePrefersDark()
  return (
    <FluentProvider theme={prefersDark ? webDarkTheme : webLightTheme} style={{ minHeight: '100vh' }}>
      <QueryClientProvider client={queryClient}>
        <AuthProvider>
          <RouterProvider router={router} />
        </AuthProvider>
      </QueryClientProvider>
    </FluentProvider>
  )
}
