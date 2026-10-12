import { MessageBar, MessageBarBody, MessageBarTitle } from '@fluentui/react-components'
import { ApiError } from '../api/client'

function describe(error: unknown): { title: string; detail?: string } {
  if (error instanceof ApiError) {
    if (error.status === 401) return { title: 'Your session has expired.', detail: 'Sign in again to continue.' }
    if (error.status === 403) return { title: 'You do not have access to this.' }
    if (error.status === 404) return { title: 'Not found.' }
    return { title: error.message, detail: error.problem?.detail }
  }
  return { title: 'Something went wrong.', detail: 'Check your connection and try again.' }
}

export function QueryError({ error }: { error: unknown }) {
  const { title, detail } = describe(error)
  return (
    <MessageBar intent="error">
      <MessageBarBody>
        <MessageBarTitle>{title}</MessageBarTitle>
        {detail}
      </MessageBarBody>
    </MessageBar>
  )
}
