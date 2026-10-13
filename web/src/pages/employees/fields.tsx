import { Dropdown, Field, Input, Option, Textarea } from '@fluentui/react-components'

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
}: BaseProps & {
  value: string
  onChange: (value: string) => void
  type?: 'text' | 'email' | 'date' | 'tel' | 'number'
  multiline?: boolean
}) {
  return (
    <Field label={label} required={required} validationMessage={error} hint={hint}>
      {multiline ? (
        <Textarea value={value} onChange={(_, d) => onChange(d.value)} />
      ) : (
        <Input type={type} value={value} onChange={(_, d) => onChange(d.value)} />
      )}
    </Field>
  )
}
