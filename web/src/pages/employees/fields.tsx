import { Button, Dropdown, Field, Input, Option, Textarea, makeStyles } from '@fluentui/react-components'
import { CalendarRegular } from '@fluentui/react-icons'
import { useRef, useState } from 'react'
import { formatDate, parseDate } from './format'

export interface SelectOption {
  value: string
  label: string
}

interface BaseProps {
  label: string
  required?: boolean
  error?: string
  hint?: string
}

const useStyles = makeStyles({
  // The browser's own date picker, opened from the calendar button. Never shown or focused itself.
  picker: { position: 'absolute', width: 0, height: 0, padding: 0, border: 0, opacity: 0, pointerEvents: 'none' },
})

/** Dropdown bound to a string value; an empty string means nothing selected. */
export function SelectField({
  label,
  value,
  options,
  onChange,
  required,
  error,
  hint,
  placeholder = 'Select…',
  clearLabel,
}: BaseProps & {
  value: string
  options: SelectOption[]
  onChange: (value: string) => void
  placeholder?: string
  /** When set, offers an option that clears the value. */
  clearLabel?: string
}) {
  const selected = options.find((o) => o.value === value)
  return (
    <Field label={label} required={required} validationMessage={error} hint={hint}>
      <Dropdown
        placeholder={placeholder}
        value={selected?.label ?? (value === '' && clearLabel ? clearLabel : '')}
        selectedOptions={[value]}
        onOptionSelect={(_, d) => onChange(d.optionValue ?? '')}
      >
        {clearLabel && <Option value="">{clearLabel}</Option>}
        {options.map((o) => (
          <Option key={o.value} value={o.value}>
            {o.label}
          </Option>
        ))}
      </Dropdown>
    </Field>
  )
}

export function TextField({
  label,
  value,
  onChange,
  required,
  error,
  hint,
  type = 'text',
  multiline,
  placeholder,
}: BaseProps & {
  value: string
  onChange: (value: string) => void
  type?: 'text' | 'email' | 'tel' | 'number'
  multiline?: boolean
  placeholder?: string
}) {
  return (
    <Field label={label} required={required} validationMessage={error} hint={hint}>
      {multiline ? (
        <Textarea value={value} placeholder={placeholder} onChange={(_, d) => onChange(d.value)} />
      ) : (
        <Input type={type} value={value} placeholder={placeholder} onChange={(_, d) => onChange(d.value)} />
      )}
    </Field>
  )
}

/**
 * A calendar date bound to an ISO "YYYY-MM-DD" value (empty when not set). Shown and typed as "14 Oct 2026" rather
 * than the browser's locale format (mm/dd/yyyy in en-US), which is ambiguous between Pakistan and the US.
 */
export function DateField({ label, value, onChange, required, error, hint }: BaseProps & { value: string; onChange: (value: string) => void }) {
  const styles = useStyles()
  const picker = useRef<HTMLInputElement>(null)
  // What the user typed; shown instead of the formatted value until they leave the field.
  const [typed, setTyped] = useState<string | null>(null)
  const invalid = typed !== null && typed.trim() !== '' && parseDate(typed) === null
  const text = typed ?? (value ? formatDate(value) : '')

  const type = (next: string) => {
    setTyped(next)
    const iso = parseDate(next)
    if (iso !== null || next.trim() === '') onChange(iso ?? '')
  }

  return (
    <Field
      label={label}
      required={required}
      validationMessage={error ?? (invalid ? 'Enter a date like 14 Oct 2026.' : undefined)}
      hint={hint}
    >
      <Input
        value={text}
        placeholder="DD Mon YYYY"
        onChange={(_, d) => type(d.value)}
        onBlur={() => !invalid && setTyped(null)}
        contentAfter={
          <>
            <Button
              appearance="transparent"
              size="small"
              icon={<CalendarRegular />}
              aria-label="Open calendar"
              onClick={() => picker.current?.showPicker()}
            />
            <input
              ref={picker}
              type="date"
              className={styles.picker}
              tabIndex={-1}
              aria-hidden
              value={value}
              onChange={(e) => {
                setTyped(null)
                onChange(e.target.value)
              }}
            />
          </>
        }
      />
    </Field>
  )
}
