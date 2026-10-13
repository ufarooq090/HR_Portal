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
import type { UseMutationResult } from '@tanstack/react-query'
import { type ReactNode, useId, useState } from 'react'
import { Link } from 'react-router'
import { type DownloadedFile, saveFile } from '../api/client'
import type { ImportResult } from '../api/types'
import { QueryError } from './QueryError'

const useStyles = makeStyles({
  page: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL, maxWidth: '1000px' },
  card: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM, padding: tokens.spacingHorizontalL },
  row: { display: 'flex', gap: tokens.spacingHorizontalM, alignItems: 'center', flexWrap: 'wrap' },
  file: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXXS },
  tableWrap: { overflowX: 'auto', maxHeight: '480px' },
})

export interface ImportSheetPageProps {
  title: string
  back: { to: string; label: string }
  /** What the file must contain, shown under "Prepare the file". */
  instructions: ReactNode
  maxRows: number
  /** Singular and plural nouns for the success message, e.g. ['employee', 'employees']. */
  noun: [string, string]
  importer: UseMutationResult<ImportResult, Error, File>
  downloadTemplate: () => Promise<DownloadedFile>
  downloadErrorReport: (file: File) => Promise<DownloadedFile>
}

/**
 * Bulk import from Excel or CSV (EMP-07, RB-12). The whole file is validated first and nothing is saved unless every
 * row is valid; the error report is the same file with an Errors column, ready to fix and upload again.
 */
export function ImportSheetPage({ title, back, instructions, maxRows, noun, importer, downloadTemplate, downloadErrorReport }: ImportSheetPageProps) {
  const styles = useStyles()
  const inputId = useId()
  const [file, setFile] = useState<File | null>(null)
  const [downloadError, setDownloadError] = useState<unknown>(null)
  const result = importer.data

  const download = async (get: () => Promise<DownloadedFile>) => {
    setDownloadError(null)
    try {
      saveFile(await get())
    } catch (e) {
      setDownloadError(e)
    }
  }

  return (
    <section className={styles.page}>
      <Title2 as="h1">{title}</Title2>
      <Body1>
        <Link to={back.to}>Back to {back.label}</Link>
      </Body1>

      <Card className={styles.card}>
        <Subtitle1 as="h2">1. Prepare the file</Subtitle1>
        <Body1>{instructions}</Body1>
        <div>
          <Button icon={<ArrowDownloadRegular />} onClick={() => void download(downloadTemplate)}>
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
            <Caption1>.xlsx or .csv, up to {maxRows.toLocaleString('en-US')} rows.</Caption1>
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
            <MessageBarTitle>Imported {result.imported === 1 ? `1 ${noun[0]}` : `${result.imported} ${noun[1]}`}.</MessageBarTitle>
            <Link to={back.to}>View them in {back.label}</Link>
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
            <Button icon={<ArrowDownloadRegular />} onClick={() => void download(() => downloadErrorReport(file))}>
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
