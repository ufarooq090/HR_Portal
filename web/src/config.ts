export type AuthMode = 'dev' | 'entra'

function readAuthMode(): AuthMode {
  const value = import.meta.env.VITE_AUTH_MODE as string | undefined
  if (value === 'dev' || value === 'entra') return value
  // DevAuth only exists on Development/Test APIs, so production builds default to Entra ID.
  return import.meta.env.DEV ? 'dev' : 'entra'
}

export const config = {
  authMode: readAuthMode(),
  entra: {
    clientId: (import.meta.env.VITE_ENTRA_CLIENT_ID as string | undefined) ?? '',
    tenantId: (import.meta.env.VITE_ENTRA_TENANT_ID as string | undefined) ?? '',
    /** Scope exposed by the API app registration, e.g. api://<api-client-id>/access_as_user */
    apiScope: (import.meta.env.VITE_ENTRA_API_SCOPE as string | undefined) ?? '',
  },
} as const
