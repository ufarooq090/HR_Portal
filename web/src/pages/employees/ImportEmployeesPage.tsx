import {
  Body1,
  Button,
  Caption1,
  Card,
  Label,
  makeStyles,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Subtitle1,
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableHeaderCell,
  TableRow,
  Title2,
  tokens,
} from '@fluentui/react-components'
import { ArrowDownloadRegular, ArrowUploadRegular } from '@fluentui/react-icons'
import { useId, useState } from 'react'
import { Link } from 'react-router'
import { saveFile } from '../../api/client'
import { downloadImportErrorReport, downloadImportTemplate, useImportEmployees } from '../../api/employees'
import { QueryError } from '../../app/QueryError'

const useStyles = makeStyles({
  page: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL, maxWidth: '1000px' },
  card: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM, padding: tokens.spacingHorizontalL },
  row: { display: 'flex', gap: tokens.spacingHorizontalM, alignItems: 'center', flexWrap: 'wrap' },
  file: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXXS },
  tableWrap: { overflowX: 'auto', maxHeight: '480px' },
})

/**
 * EMP-07: bulk import from Excel or CSV. The whole file is validated first and nothing is saved unless every
 * row is valid; the error report is the same file with an Errors column, ready to fix and upload again.
 */
export function ImportEmployeesPage() {
  const styles = useStyles()
  const inputId = useId()
  const importer = useImportEmployees()
  const [file, setFile] = useState<File | null>(null)
  const [downloadError, setDownloadError] = useState<unknown>(null)
  const result = importer.data

  const download = async (get: () => ReturnType<typeof downloadImportTemplate>) => {
    setDownloadError(null)
    try {
      saveFile(await get())
    } catch (e) {
      setDownloadError(e)
    }
  }

  return (
    <section className={styles.page}>
      <Title2 as="h1">Import employees</Title2>
      <Body1>
        <Link to="/people">Back to People</Link>
      </Body1>

      <Card className={styles.card}>
        <Subtitle1 as="h2">1. Prepare the file</Subtitle1>
        <Body1>
          Use the template's column headers. Dates as YYYY-MM-DD (or Excel date cells), legal entity, office, department
          and grade exactly as in Settings. Managers can be existing employees or other rows in the file. Salary columns
          need compensation access.
        </Body1>
        <div>
          <Button icon={<ArrowDownloadRegular />} onClick={() => void download(downloadImportTemplate)}>
            Download template
          </Button>
        </div>
      </Card>

      <Card className={styles.card}>
        <Subtitle1 as="h2">2. Upload</Subtitle1>
        <form
          className={styles.row}
          onSubmit={(e) => {
            e.preventDefault()
            if (file) importer.mutate(file)
          }}
        >
          <div className={styles.file}>
            <Label htmlFor={inputId}>Spreadsheet</Label>
            <input
              id={inputId}
              type="file"
              accept=".xlsx,.csv"
              onChange={(e) => {
                importer.reset()
                setFile(e.target.files?.[0] ?? null)
              }}
            />
            <Caption1>.xlsx or .csv, up to 2,000 rows.</Caption1>
          </div>
          <Button type="submit" appearance="primary" icon={<ArrowUploadRegular />} disabled={!file || importer.isPending}>
            {importer.isPending ? 'Checking…' : 'Import'}
          </Button>
        </form>
      </Card>

      {importer.error && <QueryError error={importer.error} />}
      {downloadError ? <QueryError error={downloadError} /> : null}

      {result && result.imported > 0 && (
        <MessageBar intent="success">
          <MessageBarBody>
            <MessageBarTitle>Imported {result.imported === 1 ? '1 employee' : `${result.imported} employees`}.</MessageBarTitle>
            <Link to="/people">View them in People</Link>
          </MessageBarBody>
        </MessageBar>
      )}

      {result && result.errors.length > 0 && file && (
        <>
          <MessageBar intent="error">
            <MessageBarBody>
              <MessageBarTitle>Nothing was imported.</MessageBarTitle>
              {result.errors.length === 1 ? '1 problem' : `${result.errors.length} problems`} in {result.totalRows} rows. Fix them
              and upload the file again.
            </MessageBarBody>
          </MessageBar>
          <div>
            <Button icon={<ArrowDownloadRegular />} onClick={() => void download(() => downloadImportErrorReport(file))}>
              Download error report
            </Button>
          </div>
          <div className={styles.tableWrap}>
            <Table aria-label="Import problems" size="small">
              <TableHeader>
                <TableRow>
                  <TableHeaderCell>Row</TableHeaderCell>
                  <TableHeaderCell>Column</TableHeaderCell>
                  <TableHeaderCell>Problem</TableHeaderCell>
                </TableRow>
              </TableHeader>
              <TableBody>
                {result.errors.map((e, i) => (
                  <TableRow key={i}>
                    <TableCell>{e.row}</TableCell>
                    <TableCell>{e.column}</TableCell>
                    <TableCell>{e.message}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        </>
      )}
    </section>
  )
}
