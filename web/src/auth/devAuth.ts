/** Local development / E2E sign-in. The API only accepts this header in Development and Test. */
export const DEV_USER_HEADER = 'X-Dev-User'
const STORAGE_KEY = 'hrportal.devUser'

type KeyValueStorage = Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>

function defaultStorage(): KeyValueStorage | undefined {
  try {
    return window.localStorage
  } catch {
    return undefined
  }
}

export function getDevUserId(storage = defaultStorage()): string | null {
  try {
    return storage?.getItem(STORAGE_KEY) ?? null
  } catch {
    return null
  }
}

export function setDevUserId(id: string, storage = defaultStorage()): void {
  try {
    storage?.setItem(STORAGE_KEY, id)
  } catch {
    // Storage unavailable (private mode): sign-in lasts for this page only.
  }
}

export function clearDevUserId(storage = defaultStorage()): void {
  try {
    storage?.removeItem(STORAGE_KEY)
  } catch {
    // ignore
  }
}

export function devAuthHeaders(userId: string): Record<string, string> {
  return { [DEV_USER_HEADER]: userId }
}
