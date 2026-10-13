import { downloadImportErrorReport, downloadImportTemplate, useImportEmployees } from '../../api/employees'
import { ImportSheetPage } from '../../app/ImportSheetPage'

/** EMP-07: bulk import of employees. */
export function ImportEmployeesPage() {
  const importer = useImportEmployees()
  return (
    <ImportSheetPage
      title="Import employees"
      back={{ to: '/people', label: 'People' }}
      instructions={
        <>
          Use the template's column headers. Dates as YYYY-MM-DD (or Excel date cells), legal entity, office, department and
          grade exactly as in Settings. Managers can be existing employees or other rows in the file. Salary columns need
          compensation access.
        </>
      }
      maxRows={2000}
      noun={['employee', 'employees']}
      importer={importer}
      downloadTemplate={downloadImportTemplate}
      downloadErrorReport={downloadImportErrorReport}
    />
  )
}
