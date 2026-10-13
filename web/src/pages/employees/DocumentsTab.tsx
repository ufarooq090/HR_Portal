import {
  Body1,
  Button,
  Caption1,
  Label,
  makeStyles,
  Spinner,
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableHeaderCell,
  TableRow,
  tokens,
} from '@fluentui/react-components'
import { ArrowDownloadRegular, ArrowUploadRegular, DeleteRegular } from '@fluentui/react-icons'
import { useId, useRef, useState } from 'react'
import { ApiError, saveFile } from '../../api/client'
import { downloadDocument, useDeleteDocument, useDocuments, useUploadDocument } from '../../api/employees'
import { type DocumentCategory, documentCategories, type EmployeeDetail } from '../../api/types'
import { QueryError } from '../../app/QueryError'
import { SelectField, TextField } from './fields'
import { formatBytes, formatDate, label } from './format'

const useStyles = makeStyles({
  section: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  upload: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))',
    gap: tokens.spacingHorizontalM,
    alignItems: 'end',
    padding: tokens.spacingHorizontalM,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
  },
  tableWrap: { overflowX: 'auto' },
  file: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXXS },
  error: { color: tokens.colorPaletteRedForeground1 },
})

/** EMP-06: files are only served through the API, after an authorisation check, and each download is audited. */
export function DocumentsTab({ employee }: { employee: EmployeeDetail }) {
  const styles = useStyles()
  const { data: documents, isLoading, error } = useDocuments(employee.id, true)
  const remove = useDeleteDocument()
  const [downloadError, setDownloadError] = useState<unknown>(null)

  const download = async (documentId: string) => {
    setDownloadError(null)
    try {
      saveFile(await downloadDocument(employee.id, documentId))
    } catch (e) {
      setDownloadError(e)
    }
  }

  return (
    <div className={styles.section}>
      {employee.permissions.canManageDocuments && <UploadForm employeeId={employee.id} />}
      {isLoading && <Spinner label="Loading documents…" />}
      {error && <QueryError error={error} />}
      {downloadError ? <QueryError error={downloadError} /> : null}
      {remove.error && <QueryError error={remove.error} />}
      {documents && documents.length === 0 && <Body1>No documents yet.</Body1>}
      {documents && documents.length > 0 && (
        <div className={styles.tableWrap}>
          <Table aria-label="Documents">
            <TableHeader>
              <TableRow>
                <TableHeaderCell>File</TableHeaderCell>
                <TableHeaderCell>Category</TableHeaderCell>
                <TableHeaderCell>Size</TableHeaderCell>
                <TableHeaderCell>Expires</TableHeaderCell>
                <TableHeaderCell>Uploaded</TableHeaderCell>
                <TableHeaderCell aria-label="Actions" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {documents.map((d) => (
                <TableRow key={d.id}>
                  <TableCell>{d.fileName}</TableCell>
                  <TableCell>{label(d.category)}</TableCell>
                  <TableCell>{formatBytes(d.sizeBytes)}</TableCell>
                  <TableCell>{formatDate(d.expiryDate)}</TableCell>
                  <TableCell>{new Date(d.uploadedAtUtc).toLocaleString()}</TableCell>
                  <TableCell>
                    <Button appearance="subtle" icon={<ArrowDownloadRegular />} aria-label={`Download ${d.fileName}`} onClick={() => void download(d.id)} />
                    {employee.permissions.canManageDocuments && (
                      <Button
                        appearance="subtle"
                        icon={<DeleteRegular />}
                        aria-label={`Delete ${d.fileName}`}
                        disabled={remove.isPending}
                        onClick={() => {
                          if (window.confirm(`Delete ${d.fileName}? This cannot be undone.`)) remove.mutate({ id: employee.id, documentId: d.id })
                        }}
                      />
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </div>
  )
}

function UploadForm({ employeeId }: { employeeId: string }) {
  const styles = useStyles()
  const upload = useUploadDocument()
  const inputId = useId()
  const input = useRef<HTMLInputElement>(null)
  const [file, setFile] = useState<File | null>(null)
  const [category, setCategory] = useState<DocumentCategory>('IdProof')
  const [expiryDate, setExpiryDate] = useState('')
  const apiError = upload.error instanceof ApiError ? upload.error : undefined

  return (
    <form
      className={styles.upload}
      aria-label="Upload document"
      onSubmit={(e) => {
        e.preventDefault()
        if (!file) return
        upload.mutate(
          { id: employeeId, file, category, expiryDate: expiryDate || null },
          {
            onSuccess: () => {
              setFile(null)
              setExpiryDate('')
              if (input.current) input.current.value = ''
            },
          },
        )
      }}
    >
      {/* A native file input: Fluent's Field only wires up its own controls' labels. */}
      <div className={styles.file}>
        <Label htmlFor={inputId}>File</Label>
        <input
          id={inputId}
          ref={input}
          type="file"
          accept=".pdf,.doc,.docx,.jpg,.jpeg,.png"
          onChange={(e) => setFile(e.target.files?.[0] ?? null)}
        />
        <Caption1>PDF, Word, JPG or PNG, up to 10 MB.</Caption1>
        {apiError?.fieldError('file') && (
          <Caption1 role="alert" className={styles.error}>
            {apiError.fieldError('file')}
          </Caption1>
        )}
      </div>
      <SelectField
        label="Category"
        value={category}
        onChange={(v) => setCategory(v as DocumentCategory)}
        options={documentCategories.map((c) => ({ value: c, label: label(c) }))}
      />
      <TextField label="Expiry date" type="date" value={expiryDate} onChange={setExpiryDate} hint="Contracts, visas, certificates." />
      <div>
        <Button type="submit" appearance="primary" icon={<ArrowUploadRegular />} disabled={!file || upload.isPending}>
          {upload.isPending ? 'Uploading…' : 'Upload'}
        </Button>
      </div>
      {upload.error && !apiError?.problem?.errors ? <QueryError error={upload.error} /> : null}
    </form>
  )
}
