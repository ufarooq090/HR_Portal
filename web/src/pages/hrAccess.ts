/** Human-readable country scope, e.g. "All countries" or "PK, US". */
export function describeCountries(scope: { allCountries: boolean; countryCodes: readonly string[] }): string {
  if (scope.allCountries) return 'All countries'
  return scope.countryCodes.length === 0 ? 'No countries' : [...scope.countryCodes].sort().join(', ')
}
