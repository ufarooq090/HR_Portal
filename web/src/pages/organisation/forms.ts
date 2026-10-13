/** Field kinds the reference-data editor can render. Names match the API's camelCase request properties. */
export type FieldKind = 'text' | 'code' | 'country' | 'timeZone' | 'number' | 'optionalNumber' | 'active'

export interface FieldDef {
  name: string
  label: string
  kind: FieldKind
  required?: boolean
  /** Shown only when creating, e.g. a country that is fixed once the item exists. */
  createOnly?: boolean
  hint?: string
}

export type FormValues = Record<string, string | boolean>

/** Form state for a new item, or for editing an existing one (nulls become empty strings). */
export function initialValues(fields: readonly FieldDef[], item?: object): FormValues {
  const source = (item ?? {}) as Record<string, unknown>
  const values: FormValues = {}
  for (const field of fields) {
    const value = source[field.name]
    if (field.kind === 'active') values[field.name] = item ? value !== false : true
    else values[field.name] = value === null || value === undefined ? '' : String(value)
  }
  return values
}

/**
 * Request body for the API. Create-only fields are left out when editing, and the active flag when creating
 * (new items always start active). Invalid numbers are passed through as null so the API reports the field.
 */
export function toRequestBody(fields: readonly FieldDef[], values: FormValues, mode: 'create' | 'edit'): Record<string, unknown> {
  const body: Record<string, unknown> = {}
  for (const field of fields) {
    if (mode === 'edit' && field.createOnly) continue
    if (mode === 'create' && field.kind === 'active') continue

    const raw = values[field.name]
    if (field.kind === 'active') {
      body[field.name] = raw === true
      continue
    }

    const text = typeof raw === 'string' ? raw.trim() : ''
    switch (field.kind) {
      case 'number':
        body[field.name] = parseWholeNumber(text) ?? 0
        break
      case 'optionalNumber':
        body[field.name] = text === '' ? null : parseWholeNumber(text)
        break
      case 'code':
      case 'country':
        body[field.name] = text.toUpperCase()
        break
      default:
        body[field.name] = text === '' && !field.required ? null : text
    }
  }
  return body
}

function parseWholeNumber(text: string): number | null {
  return /^-?\d+$/.test(text) ? Number.parseInt(text, 10) : null
}

/** Field names in the form that are missing a required value. */
export function missingRequired(fields: readonly FieldDef[], values: FormValues, mode: 'create' | 'edit'): string[] {
  return fields
    .filter((f) => f.required && !(mode === 'edit' && f.createOnly))
    .filter((f) => typeof values[f.name] !== 'string' || (values[f.name] as string).trim() === '')
    .map((f) => f.name)
}

/** IANA time zones offered as suggestions; the API validates whatever is typed. */
export function timeZoneSuggestions(): string[] {
  try {
    return Intl.supportedValuesOf('timeZone')
  } catch {
    return ['Asia/Karachi', 'America/New_York', 'America/Chicago', 'America/Denver', 'America/Los_Angeles']
  }
}
