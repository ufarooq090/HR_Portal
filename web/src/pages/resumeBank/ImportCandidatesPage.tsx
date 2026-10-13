import { downloadCandidateImportErrorReport, downloadCandidateImportTemplate, useImportCandidates } from '../../api/resumeBank'
import { ImportSheetPage } from '../../app/ImportSheetPage'

/** RB-12: HR's existing candidate sheets into the Resume Bank. */
export function ImportCandidatesPage() {
  const importer = useImportCandidates()
  return (
    <ImportSheetPage
      title="Import candidates"
      back={{ to: '/resume-bank', label: 'Resume Bank' }}
      instructions={
        <>
          Use the template's column headers. Each candidate needs a name, a country, and an email or phone number. People already
          in the portal (same email or phone, candidates or employees) are reported, not added twice. Skills and tags are separated
          by commas or semicolons; dates as YYYY-MM-DD. Notes become a note on the candidate. CVs are uploaded on each candidate's
          page afterwards.
        </>
      }
      maxRows={5000}
      noun={['candidate', 'candidates']}
      importer={importer}
      downloadTemplate={downloadCandidateImportTemplate}
      downloadErrorReport={downloadCandidateImportErrorReport}
    />
  )
}
