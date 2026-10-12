import { createBrowserRouter } from 'react-router'
import { CountrySettingsPage } from '../pages/CountrySettingsPage'
import { HomePage } from '../pages/HomePage'
import { NotFoundPage } from '../pages/NotFoundPage'
import { AppShell } from './AppShell'

export const router = createBrowserRouter([
  {
    element: <AppShell />,
    children: [
      { index: true, element: <HomePage /> },
      { path: 'settings/countries', element: <CountrySettingsPage /> },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
])
