import { Body1, Title2 } from '@fluentui/react-components'
import { Link } from 'react-router'

export function NotFoundPage() {
  return (
    <section>
      <Title2 as="h1">Page not found</Title2>
      <Body1>
        <Link to="/">Back to home</Link>
      </Body1>
    </section>
  )
}
