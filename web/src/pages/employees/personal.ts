import type { PersonalDetails } from '../../api/types'

export type FieldError = (field: string) => string | undefined

export const emptyPersonal: PersonalDetails = {
  personalEmail: null,
  phone: null,
  dateOfBirth: null,
  addressLine1: null,
  addressLine2: null,
  city: null,
  region: null,
  postalCode: null,
  addressCountryCode: null,
  nationalId: null,
  emergencyContacts: [],
}

/** Trims emergency contacts and turns empty strings into nulls before sending. */
export function cleanPersonal(value: PersonalDetails): PersonalDetails {
  const blankToNull = (v: string | null) => (v?.trim() ? v.trim() : null)
  return {
    personalEmail: blankToNull(value.personalEmail),
    phone: blankToNull(value.phone),
    dateOfBirth: blankToNull(value.dateOfBirth),
    addressLine1: blankToNull(value.addressLine1),
    addressLine2: blankToNull(value.addressLine2),
    city: blankToNull(value.city),
    region: blankToNull(value.region),
    postalCode: blankToNull(value.postalCode),
    addressCountryCode: blankToNull(value.addressCountryCode),
    nationalId: blankToNull(value.nationalId),
    emergencyContacts: value.emergencyContacts.map((c) => ({ name: c.name.trim(), relationship: c.relationship.trim(), phone: c.phone.trim() })),
  }
}
