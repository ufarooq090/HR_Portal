import { createBrowserRouter } from 'react-router'
import { HomePage } from '../pages/HomePage'
import { HrAccessPage } from '../pages/HrAccessPage'
import { NotFoundPage } from '../pages/NotFoundPage'
import {
  CountriesPage,
  DepartmentsPage,
  GradesPage,
  LegalEntitiesPage,
  OfficeLocationsPage,
} from '../pages/organisation/OrganisationPages'
import { PeoplePage } from '../pages/PeoplePage'
import { EmployeeProfilePage } from '../pages/employees/EmployeeProfilePage'
import { ImportEmployeesPage } from '../pages/employees/ImportEmployeesPage'
import { NewEmployeePage } from '../pages/employees/NewEmployeePage'
import { AppShell } from './AppShell'

export const router = createBrowserRouter([
  {
    element: <AppShell />,
    children: [
      { index: true, element: <HomePage /> },
      { path: 'people', element: <PeoplePage /> },
      { path: 'people/new', element: <NewEmployeePage /> },
      { path: 'people/import', element: <ImportEmployeesPage /> },
      { path: 'people/:id', element: <EmployeeProfilePage /> },
      { path: 'settings/countries', element: <CountriesPage /> },
      { path: 'settings/legal-entities', element: <LegalEntitiesPage /> },
      { path: 'settings/locations', element: <OfficeLocationsPage /> },
      { path: 'settings/departments', element: <DepartmentsPage /> },
      { path: 'settings/grades', element: <GradesPage /> },
      { path: 'settings/hr-access', element: <HrAccessPage /> },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
])
