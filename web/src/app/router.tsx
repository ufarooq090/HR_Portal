import { createBrowserRouter } from 'react-router'
import { HomePage } from '../pages/HomePage'
import { HrAccessPage } from '../pages/HrAccessPage'
import { NotFoundPage } from '../pages/NotFoundPage'
import { ImportCandidatesPage } from '../pages/resumeBank/ImportCandidatesPage'
import { ResumeBankPage } from '../pages/resumeBank/ResumeBankPage'
import { ResumeBankProfilePage } from '../pages/resumeBank/ResumeBankProfilePage'
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
import { ApprovalChainsPage } from '../pages/requisitions/ApprovalChainsPage'
import { ApprovalsPage } from '../pages/requisitions/ApprovalsPage'
import { RequisitionDetailPage } from '../pages/requisitions/RequisitionDetailPage'
import { RequisitionFormPage } from '../pages/requisitions/RequisitionFormPage'
import { RequisitionsPage } from '../pages/requisitions/RequisitionsPage'
import { ApplicationPage } from '../pages/recruiting/ApplicationPage'
import { CandidatesPage, MyInterviewsPage } from '../pages/recruiting/CandidatesPage'
import { PipelinePage } from '../pages/recruiting/PipelinePage'
import { RejectionReasonsPage } from '../pages/recruiting/RejectionReasonsPage'
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
      { path: 'requisitions', element: <RequisitionsPage /> },
      { path: 'requisitions/new', element: <RequisitionFormPage /> },
      { path: 'requisitions/:id', element: <RequisitionDetailPage /> },
      { path: 'requisitions/:id/edit', element: <RequisitionFormPage /> },
      { path: 'approvals', element: <ApprovalsPage /> },
      { path: 'requisitions/:id/pipeline', element: <PipelinePage /> },
      { path: 'applications/:id', element: <ApplicationPage /> },
      { path: 'candidates', element: <CandidatesPage /> },
      { path: 'interviews', element: <MyInterviewsPage /> },
      { path: 'resume-bank', element: <ResumeBankPage /> },
      { path: 'resume-bank/import', element: <ImportCandidatesPage /> },
      { path: 'resume-bank/:personId', element: <ResumeBankProfilePage /> },
      { path: 'settings/rejection-reasons', element: <RejectionReasonsPage /> },
      { path: 'settings/approval-chains', element: <ApprovalChainsPage /> },
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
