import { Badge, Body1, Spinner, Title2, makeStyles, tokens } from '@fluentui/react-components'
import { useMe } from '../api/queries'
import { QueryError } from '../app/QueryError'

const useStyles = makeStyles({
  page: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  roles: { display: 'flex', flexWrap: 'wrap', gap: tokens.spacingHorizontalS },
})

export function HomePage() {
  const styles = useStyles()
  const { data: me, isLoading, error } = useMe()

  if (isLoading) return <Spinner label="Loading…" />
  if (error) return <QueryError error={error} />

  return (
    <section className={styles.page}>
      <Title2 as="h1">Welcome, {me?.displayName}</Title2>
      <Body1>Your roles:</Body1>
      <div className={styles.roles}>
        {me?.roles.map((role) => (
          <Badge key={role} appearance="tint">
            {role}
          </Badge>
        ))}
      </div>
    </section>
  )
}
