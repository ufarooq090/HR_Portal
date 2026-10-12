import { describe, expect, it, vi } from 'vitest'
import { ApiError, createApiClient } from './client'

function jsonResponse(status: number, body: unknown, contentType = 'application/json'): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': contentType } })
}

describe('createApiClient', () => {
  it('adds the auth headers to every request', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(jsonResponse(200, { ok: true }))
    const client = createApiClient(async () => ({ 'X-Dev-User': 'dev-pk-hradmin' }), fetchMock)

    await client.get('/api/me')

    const init = fetchMock.mock.calls[0]![1]!
    expect((init.headers as Record<string, string>)['X-Dev-User']).toBe('dev-pk-hradmin')
    expect(init.method).toBe('GET')
  })

  it('sends JSON bodies and parses JSON responses', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(jsonResponse(200, { probationMonths: 6 }))
    const client = createApiClient(async () => ({}), fetchMock)

    const result = await client.put<{ probationMonths: number }>('/api/organisation/countries/US', { probationMonths: 6 })

    const init = fetchMock.mock.calls[0]![1]!
    expect(init.body).toBe('{"probationMonths":6}')
    expect((init.headers as Record<string, string>)['Content-Type']).toBe('application/json')
    expect(result.probationMonths).toBe(6)
  })

  it('throws ApiError with validation problem details', async () => {
    const problem = { title: 'One or more validation errors occurred.', status: 400, errors: { probationMonths: ['Too long'] } }
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(jsonResponse(400, problem, 'application/problem+json'))
    const client = createApiClient(async () => ({}), fetchMock)

    const error = await client.put('/x', {}).catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).status).toBe(400)
    expect((error as ApiError).fieldError('ProbationMonths')).toBe('Too long')
  })

  it('throws ApiError without details for non-JSON errors', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(new Response('', { status: 403 }))
    const client = createApiClient(async () => ({}), fetchMock)

    const error = (await client.get('/x').catch((e: unknown) => e)) as ApiError

    expect(error.status).toBe(403)
    expect(error.problem).toBeUndefined()
  })

  it('returns undefined for 204 No Content', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(new Response(null, { status: 204 }))
    const client = createApiClient(async () => ({}), fetchMock)

    await expect(client.delete('/x')).resolves.toBeUndefined()
  })
})
