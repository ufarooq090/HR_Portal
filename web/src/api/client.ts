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

/** A downloaded file. Downloads go through fetch because every API call needs the auth headers. */
export interface DownloadedFile {
  blob: Blob
  fileName: string
}

export interface ApiClient {
  get<T>(path: string): Promise<T>
  put<T>(path: string, body: unknown): Promise<T>
  post<T>(path: string, body: unknown): Promise<T>
  /** Multipart upload; the browser sets the boundary header. */
  postForm<T>(path: string, form: FormData): Promise<T>
  delete(path: string): Promise<void>
  download(path: string, form?: FormData): Promise<DownloadedFile>
}

export function createApiClient(getHeaders: HeaderProvider, fetchImpl: typeof fetch = fetch): ApiClient {
  async function request(method: string, path: string, body?: unknown, accept = 'application/json'): Promise<Response> {
    const headers: Record<string, string> = { Accept: accept, ...(await getHeaders()) }
    const isForm = body instanceof FormData
    if (body !== undefined && !isForm) headers['Content-Type'] = 'application/json'

    const response = await fetchImpl(path, {
      method,
      headers,
      body: body === undefined ? undefined : isForm ? body : JSON.stringify(body),
    })

    if (!response.ok) {
      throw new ApiError(response.status, await readProblem(response))
    }

    return response
  }

  async function send<T>(method: string, path: string, body?: unknown): Promise<T> {
    const response = await request(method, path, body)
    if (response.status === 204 || response.headers.get('Content-Length') === '0') {
      return undefined as T
    }

    return (await response.json()) as T
  }

  return {
    get: (path) => send('GET', path),
    put: (path, body) => send('PUT', path, body),
    post: (path, body) => send('POST', path, body),
    postForm: (path, form) => send('POST', path, form),
    delete: (path) => send('DELETE', path),
    download: async (path, form) => {
      const response = await request(form ? 'POST' : 'GET', path, form, '*/*')
      return { blob: await response.blob(), fileName: fileNameFrom(response.headers.get('Content-Disposition')) }
    },
  }
}

/** File name from a Content-Disposition header, preferring the RFC 5987 UTF-8 form. */
export function fileNameFrom(contentDisposition: string | null): string {
  if (!contentDisposition) return 'download'
  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(contentDisposition)
  if (encoded?.[1]) return decodeURIComponent(encoded[1])
  const plain = /filename="?([^";]+)"?/i.exec(contentDisposition)
  return plain?.[1] ?? 'download'
}

/** Hands a downloaded file to the browser's save dialog. */
export function saveFile({ blob, fileName }: DownloadedFile): void {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  document.body.appendChild(link)
  link.click()
  link.remove()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
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
