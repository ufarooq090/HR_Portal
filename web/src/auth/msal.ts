import {
  InteractionRequiredAuthError,
  PublicClientApplication,
  type Configuration,
} from '@azure/msal-browser'
import { config } from '../config'

export function createMsalInstance(): PublicClientApplication {
  const { clientId, tenantId } = config.entra
  if (!clientId || !tenantId || !config.entra.apiScope) {
    throw new Error('Entra ID is not configured. Set VITE_ENTRA_CLIENT_ID, VITE_ENTRA_TENANT_ID and VITE_ENTRA_API_SCOPE.')
  }

  const msalConfig: Configuration = {
    auth: {
      clientId,
      authority: `https://login.microsoftonline.com/${tenantId}`,
      redirectUri: window.location.origin,
      postLogoutRedirectUri: window.location.origin,
    },
    cache: { cacheLocation: 'sessionStorage' },
  }
  return new PublicClientApplication(msalConfig)
}

export const loginRequest = { scopes: [config.entra.apiScope] }

/** Bearer header for the API; falls back to an interactive redirect when consent or MFA is needed. */
export async function entraAuthHeaders(instance: PublicClientApplication): Promise<Record<string, string>> {
  const account = instance.getActiveAccount() ?? instance.getAllAccounts()[0]
  if (!account) {
    await instance.loginRedirect(loginRequest)
    return {}
  }

  try {
    const result = await instance.acquireTokenSilent({ ...loginRequest, account })
    return { Authorization: `Bearer ${result.accessToken}` }
  } catch (error) {
    if (error instanceof InteractionRequiredAuthError) {
      await instance.acquireTokenRedirect({ ...loginRequest, account })
      return {}
    }
    throw error
  }
}
