import { useEffect, useState, type ReactNode } from 'react'
import { MsalAuthenticationTemplate, MsalProvider } from '@azure/msal-react'
import { InteractionType, type PublicClientApplication } from '@azure/msal-browser'
import { MessageBar, MessageBarBody, MessageBarTitle, Spinner } from '@fluentui/react-components'
import { useQueryClient } from '@tanstack/react-query'
import { setAuthHeaderProvider } from '../api/client'
import { config } from '../config'
import { AuthContext } from './AuthContext'
import { clearDevUserId, devAuthHeaders, getDevUserId, setDevUserId } from './devAuth'
import { DevSignInPage } from './DevSignInPage'
import { createMsalInstance, entraAuthHeaders, loginRequest } from './msal'

/** Renders children only once a user is signed in, via DevAuth locally or Entra ID when deployed. */
export function AuthProvider({ children }: { children: ReactNode }) {
  return config.authMode === 'dev' ? <DevAuthGate>{children}</DevAuthGate> : <EntraAuthGate>{children}</EntraAuthGate>
}

function DevAuthGate({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()
  const [userId, setUserId] = useState(() => getDevUserId())

  if (!userId) {
    return (
      <DevSignInPage
        onSignIn={(id) => {
          setDevUserId(id)
          queryClient.clear()
          setUserId(id)
        }}
      />
    )
  }

  setAuthHeaderProvider(async () => devAuthHeaders(userId))

  const signOut = () => {
    clearDevUserId()
    setAuthHeaderProvider(async () => ({}))
    queryClient.clear()
    setUserId(null)
  }

  return <AuthContext.Provider value={{ mode: 'dev', signOut }}>{children}</AuthContext.Provider>
}

type EntraState =
  | { status: 'loading' }
  | { status: 'ready'; instance: PublicClientApplication }
  | { status: 'error'; message: string }

function EntraAuthGate({ children }: { children: ReactNode }) {
  const [state, setState] = useState<EntraState>({ status: 'loading' })

  useEffect(() => {
    let cancelled = false
    void (async () => {
      try {
        const instance = createMsalInstance()
        await instance.initialize()
        const redirect = await instance.handleRedirectPromise()
        const account = redirect?.account ?? instance.getAllAccounts()[0]
        if (account) instance.setActiveAccount(account)
        if (!cancelled) setState({ status: 'ready', instance })
      } catch (error) {
        if (!cancelled) setState({ status: 'error', message: error instanceof Error ? error.message : String(error) })
      }
    })()
    return () => {
      cancelled = true
    }
  }, [])

  if (state.status === 'loading') return <Spinner label="Signing in…" style={{ marginTop: 96 }} />
  if (state.status === 'error') {
    return (
      <MessageBar intent="error" style={{ margin: 24 }}>
        <MessageBarBody>
          <MessageBarTitle>Sign-in is not available</MessageBarTitle>
          {state.message}
        </MessageBarBody>
      </MessageBar>
    )
  }

  const { instance } = state
  setAuthHeaderProvider(() => entraAuthHeaders(instance))
  const signOut = () => void instance.logoutRedirect()

  return (
    <MsalProvider instance={instance}>
      <MsalAuthenticationTemplate
        interactionType={InteractionType.Redirect}
        authenticationRequest={loginRequest}
        loadingComponent={() => <Spinner label="Signing in…" style={{ marginTop: 96 }} />}
      >
        <AuthContext.Provider value={{ mode: 'entra', signOut }}>{children}</AuthContext.Provider>
      </MsalAuthenticationTemplate>
    </MsalProvider>
  )
}
