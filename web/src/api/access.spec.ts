import { beforeEach, expect, test, vi } from 'vitest'

beforeEach(() => {
  vi.resetModules()
  vi.restoreAllMocks()
})

test('session probe shows the password gate when the server requires access', async () => {
  vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({ required: true, authenticated: false }), { status: 200 }))
  const access = await import('./access')

  await access.refreshAccess()

  expect(access.hasAccess.value).toBe(false)
  expect(access.accessReady.value).toBe(true)
})

test('session probe enters directly when access is not required in development', async () => {
  vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({ required: false, authenticated: false }), { status: 200 }))
  const access = await import('./access')

  await access.refreshAccess()

  expect(access.hasAccess.value).toBe(true)
})

test('failed session probe falls back to the password gate instead of exposing the ledger', async () => {
  vi.spyOn(globalThis, 'fetch').mockRejectedValue(new TypeError('offline'))
  const access = await import('./access')

  await access.refreshAccess()

  expect(access.hasAccess.value).toBe(false)
})

test('login stores no password and marks access after the server sets its cookie', async () => {
  const fetch = vi.spyOn(globalThis, 'fetch')
    .mockResolvedValue(new Response(JSON.stringify({ authenticated: true }), { status: 200 }))
  const access = await import('./access')

  await access.login('correct horse battery staple')

  expect(access.hasAccess.value).toBe(true)
  expect(fetch.mock.calls[0]![0]).toContain('/api/access/login')
  expect(fetch.mock.calls[0]![1]?.credentials).toBe('include')
  expect(fetch.mock.calls[0]![1]?.body).toContain('correct horse battery staple')
  expect(sessionStorage.length).toBe(0)
})

test('login translates rejected and throttled responses', async () => {
  vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(new Response('{}', { status: 401 }))
  const access = await import('./access')

  await expect(access.login('wrong')).rejects.toThrow('访问口令不正确')

  vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(new Response('{}', { status: 429 }))
  await expect(access.login('wrong')).rejects.toThrow('尝试次数过多')
  expect(access.hasAccess.value).toBe(false)
})

test('logout closes the server session and locks the app locally', async () => {
  const fetch = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(null, { status: 204 }))
  const access = await import('./access')
  access.hasAccess.value = true

  await access.logout()

  expect(fetch.mock.calls[0]![1]?.method).toBe('POST')
  expect(access.hasAccess.value).toBe(false)
})
