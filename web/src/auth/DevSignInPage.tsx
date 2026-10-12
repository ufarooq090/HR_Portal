import {
  Badge,
  Button,
  Card,
  CardHeader,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Spinner,
  Text,
  Title2,
  makeStyles,
  tokens,
} from '@fluentui/react-components'
import { useDevUsers } from '../api/queries'
import type { DevUser } from '../api/types'

const useStyles = makeStyles({
  page: {
    maxWidth: '960px',
    margin: '0 auto',
    padding: `${tokens.spacingVerticalXXL} ${tokens.spacingHorizontalL}`,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
  },
  columns: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))',
    gap: tokens.spacingHorizontalL,
  },
  list: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXS },
  user: { justifyContent: 'space-between', width: '100%' },
})

const countryNames: Record<string, string> = { PK: 'Pakistan', US: 'United States' }

/** Picks a seeded test user. Only shown when the SPA runs with VITE_AUTH_MODE=dev. */
export function DevSignInPage({ onSignIn }: { onSignIn: (userId: string) => void }) {
  const styles = useStyles()
  const { data: users, isLoading, error } = useDevUsers()

  const byCountry = new Map<string, DevUser[]>()
  for (const user of users ?? []) {
    byCountry.set(user.countryCode, [...(byCountry.get(user.countryCode) ?? []), user])
  }

  return (
    <main className={styles.page}>
      <Title2 as="h1">HR Portal — development sign-in</Title2>
      <MessageBar intent="warning">
        <MessageBarBody>
          <MessageBarTitle>Local development only.</MessageBarTitle>
          Deployed environments sign in with Microsoft Entra ID; the API refuses to start with DevAuth anywhere else.
        </MessageBarBody>
      </MessageBar>

      {isLoading && <Spinner label="Loading test users…" />}
      {error && (
        <MessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>Could not reach the API.</MessageBarTitle>
            Start it with <code>dotnet run --project src/HrPortal.Api</code> and make sure DevAuth is enabled.
          </MessageBarBody>
        </MessageBar>
      )}

      <div className={styles.columns}>
        {[...byCountry.entries()].map(([country, countryUsers]) => (
          <Card key={country}>
            <CardHeader header={<Text weight="semibold">{countryNames[country] ?? country}</Text>} />
            <div className={styles.list}>
              {countryUsers.map((user) => (
                <Button
                  key={user.id}
                  appearance="subtle"
                  className={styles.user}
                  onClick={() => onSignIn(user.id)}
                  aria-label={`Sign in as ${user.displayName}`}
                >
                  <span>{user.roles[0]}</span>
                  <Badge appearance="outline" color="informative">
                    {user.id}
                  </Badge>
                </Button>
              ))}
            </div>
          </Card>
        ))}
      </div>
    </main>
  )
}
