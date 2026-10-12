import type { ProblemDetails } from './types'

export type HeaderProvider = () => Promise<Record<string, string>>

export class ApiError extends Error {
  readonly status: number
  readonly problem: ProblemDetails | undefined

  constructor(status: number, problem: ProblemDetails | undefined) {
    super(problem?.title ?? `Request failed with status ${status}`)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
  }

  /** First validation message for a field, matched case-insensitively (the API uses camelCase names). */
  fieldError(field: string): string | undefined {
    const errors = this.problem?.errors ?? {}
    const key = Object.keys(errors).find((k) => k.toLowerCase() === field.toLowerCase())
    return key ? errors[key]?.[0] : undefined
  }
}

export interface ApiClient {
  get<T>(path: string): Promise<T>
  put<T>(path: string, body: unknown): Promise<T>
  post<T>(path: string, body: unknown): Promise<T>
  delete(path: string): Promise<void>
}

export function createApiClient(getHeaders: HeaderProvider, fetchImpl: typeof fetch = fetch): ApiClient {
  async function send<T>(method: string, path: string, body?: unknown): Promise<T> {
    const headers: Record<string, string> = { Accept: 'application/json', ...(await getHeaders()) }
    if (body !== undefined) headers['Content-Type'] = 'application/json'

    const response = await fetchImpl(path, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    })

    if (!response.ok) {
      throw new ApiError(response.status, await readProblem(response))
    }

    if (response.status === 204 || response.headers.get('Content-Length') === '0') {
      return undefined as T
    }

    return (await response.json()) as T
  }

  return {
    get: (path) => send('GET', path),
    put: (path, body) => send('PUT', path, body),
    post: (path, body) => send('POST', path, body),
    delete: (path) => send('DELETE', path),
  }
}

async function readProblem(response: Response): Promise<ProblemDetails | undefined> {
  const contentType = response.headers.get('Content-Type') ?? ''
  if (!contentType.includes('json')) return undefined
  try {
    return (await response.json()) as ProblemDetails
  } catch {
    return undefined
  }
}

let headerProvider: HeaderProvider = async () => ({})

/** Called by the auth layer once a user is signed in. */
export function setAuthHeaderProvider(provider: HeaderProvider): void {
  headerProvider = provider
}

/** Shared client used by query hooks; adds the current user's auth headers to every request. */
export const api: ApiClient = createApiClient(() => headerProvider())

/** Client without auth headers, for the few anonymous endpoints. */
export const anonymousApi: ApiClient = createApiClient(async () => ({}))
