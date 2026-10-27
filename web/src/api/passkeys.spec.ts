import { beforeEach, expect, test, vi } from 'vitest'

const webauthn = vi.hoisted(() => ({
  supported: vi.fn(() => true),
  startRegistration: vi.fn(),
  startAuthentication: vi.fn()
}))

vi.mock('@simplewebauthn/browser', () => ({
  browserSupportsWebAuthn: webauthn.supported,
  startRegistration: webauthn.startRegistration,
  startAuthentication: webauthn.startAuthentication
}))

beforeEach(() => {
  vi.resetModules()
  vi.restoreAllMocks()
  webauthn.supported.mockReturnValue(true)
  webauthn.startRegistration.mockReset()
  webauthn.startAuthentication.mockReset()
})

test('registration exchanges server options for a stored passkey', async () => {
  const fetch = vi.spyOn(globalThis, 'fetch')
    .mockResolvedValueOnce(new Response(JSON.stringify({ challenge: 'abc', rp: { id: 'localhost' } }), { status: 200 }))
    .mockResolvedValueOnce(new Response(JSON.stringify({ id: 'server-id', label: 'Mac', createdAt: '2026-10-27T00:00:00Z', lastUsedAt: null }), { status: 200 }))
  webauthn.startRegistration.mockResolvedValue({ id: 'credential-id', response: {} })
  const passkeys = await import('./passkeys')

  const created = await passkeys.registerPasskey('Mac')

  expect(created.label).toBe('Mac')
  expect(webauthn.startRegistration).toHaveBeenCalledWith({ optionsJSON: { challenge: 'abc', rp: { id: 'localhost' } } })
  expect(fetch.mock.calls[1]![0]).toContain('/api/access/passkeys/registration/complete')
  expect(JSON.parse(fetch.mock.calls[1]![1]!.body as string)).toEqual({
    response: { id: 'credential-id', response: {} },
    label: 'Mac'
  })
})

test('passkey login unlocks the app after server verification', async () => {
  const fetch = vi.spyOn(globalThis, 'fetch')
    .mockResolvedValueOnce(new Response(JSON.stringify({ challenge: 'def' }), { status: 200 }))
    .mockResolvedValueOnce(new Response(JSON.stringify({ authenticated: true }), { status: 200 }))
  webauthn.startAuthentication.mockResolvedValue({ id: 'credential-id', response: {} })
  const access = await import('./access')
  const passkeys = await import('./passkeys')

  await passkeys.signInWithPasskey()

  expect(access.hasAccess.value).toBe(true)
  expect(fetch.mock.calls[0]![0]).toContain('/api/access/passkeys/login/options')
  expect(fetch.mock.calls[1]![0]).toContain('/api/access/passkeys/login/complete')
})

test('cancelled biometric prompt is reported without exposing a generic failure', async () => {
  vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response('{}', { status: 200 }))
  const cancelled = new Error('user cancelled')
  cancelled.name = 'NotAllowedError'
  webauthn.startAuthentication.mockRejectedValue(cancelled)
  const passkeys = await import('./passkeys')

  await expect(passkeys.signInWithPasskey()).rejects.toThrow('已取消指纹 / Face ID 验证')
})

test('unsupported browsers fall back to the access password', async () => {
  webauthn.supported.mockReturnValue(false)
  const passkeys = await import('./passkeys')

  await expect(passkeys.signInWithPasskey()).rejects.toThrow('当前浏览器不支持 Passkey')
})

test('deleting a passkey uses the registered credential id', async () => {
  const fetch = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(null, { status: 204 }))
  const passkeys = await import('./passkeys')

  await passkeys.deletePasskey('4c8b5f7e-1111-2222-3333-444455556666')

  expect(fetch.mock.calls[0]![0]).toContain('/api/access/passkeys/4c8b5f7e-1111-2222-3333-444455556666')
  expect(fetch.mock.calls[0]![1]?.method).toBe('DELETE')
})
