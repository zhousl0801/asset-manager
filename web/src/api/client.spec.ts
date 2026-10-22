import { beforeEach, expect, test, vi } from 'vitest'
import { apiRequest, readPendingMutations } from './client'

beforeEach(() => {
  localStorage.clear()
  vi.restoreAllMocks()
})

test('lost save response is recovered by request ID instead of showing an unconfirmed amount', async () => {
  const fetch = vi.spyOn(globalThis, 'fetch')
    .mockRejectedValueOnce(new TypeError('response lost'))
    .mockResolvedValueOnce(new Response(JSON.stringify({ operation: 'provident.deposit', result: { balance: 100, activity: { amount: 100 } } }), { status: 200 }))
  const result = await apiRequest<{ balance: number }>('/api/ledger/provident/deposits', {
    method: 'POST', body: JSON.stringify({ amount: 100, requestId: 'b8388a71-7021-4142-b3a0-12710f203fde' })
  })
  expect(result.balance).toBe(100)
  expect(fetch.mock.calls[1]![0]).toContain('/api/ledger/requests/b8388a71-7021-4142-b3a0-12710f203fde')
})

test('unknown result preserves original request and prevents a changed amount being retried', async () => {
  const fetch = vi.spyOn(globalThis, 'fetch').mockRejectedValue(new TypeError('offline'))
  const input = { amount: 100, requestId: 'b8388a71-7021-4142-b3a0-12710f203fde' }
  await expect(apiRequest('/api/ledger/provident/deposits', { method: 'POST', body: JSON.stringify(input) })).rejects.toThrow()
  const calls = fetch.mock.calls.length
  await expect(apiRequest('/api/ledger/provident/deposits', { method: 'POST', body: JSON.stringify({ ...input, amount: 200 }) })).rejects.toThrow('待核实')
  expect(fetch).toHaveBeenCalledTimes(calls)
  expect(localStorage.length).toBeGreaterThan(0)
})

test('pending request survives module reload and can be queried without resending', async () => {
  vi.spyOn(globalThis, 'fetch').mockRejectedValue(new TypeError('offline'))
  const requestId = '4108dc88-73c5-4e5e-9b04-92705bdd2dc4'
  await expect(apiRequest('/api/ledger/cash/reconcile', { method: 'POST', body: JSON.stringify({ actualBalance: 900, expectedVersion: 0, requestId }) })).rejects.toThrow('待核实')
  vi.resetModules()
  const refreshed = await import('./client')
  const request = refreshed.readPendingMutations()[0]!
  const fetch = vi.spyOn(globalThis, 'fetch').mockClear().mockResolvedValue(new Response(JSON.stringify({ result: { cashBalance: 900 } }), { status: 200 }))

  await expect(refreshed.recoverMutation(request, false)).resolves.toEqual({ cashBalance: 900 })
  expect(fetch).toHaveBeenCalledTimes(1)
  expect(fetch.mock.calls[0]![1]?.method).toBeUndefined()
  expect(refreshed.readPendingMutations()).toEqual([])
})

test('known validation failure permits corrected content with the same unused ID', async () => {
  vi.spyOn(globalThis, 'fetch')
    .mockResolvedValueOnce(new Response(JSON.stringify({ detail: '金额无效' }), { status: 400 }))
    .mockResolvedValueOnce(new Response(JSON.stringify({ balance: 100 }), { status: 200 }))
  const input = { amount: -1, requestId: '40b4ecb9-fca5-4cd4-a551-e53e3bdf3c2b' }
  await expect(apiRequest('/api/ledger/provident/deposits', { method: 'POST', body: JSON.stringify(input) })).rejects.toThrow('金额无效')
  await expect(apiRequest('/api/ledger/provident/deposits', { method: 'POST', body: JSON.stringify({ ...input, amount: 100 }) })).resolves.toEqual({ balance: 100 })
})

test('retry uses the persisted original body after a not-yet-found lookup', async () => {
  const { recoverMutation } = await import('./client')
  const original = { requestId: 'e39f2d03-b28b-48cc-af1b-4d8698239082', path: '/api/ledger/entries', method: 'POST', body: JSON.stringify({ requestId: 'e39f2d03-b28b-48cc-af1b-4d8698239082', amount: 100 }) }
  localStorage.setItem(`asset-keeper.pending.${original.requestId}`, JSON.stringify(original))
  const fetch = vi.spyOn(globalThis, 'fetch')
    .mockResolvedValueOnce(new Response('{}', { status: 404 }))
    .mockResolvedValueOnce(new Response(JSON.stringify({ cashBalance: 100 }), { status: 200 }))
  await expect(recoverMutation(original, true)).resolves.toEqual({ cashBalance: 100 })
  expect(fetch.mock.calls[1]![1]?.body).toBe(original.body)
  expect(localStorage.length).toBe(0)
})

test('nested history confirmations use the same pending journal and recovery receipt', async () => {
  vi.spyOn(globalThis, 'fetch').mockRejectedValueOnce(new TypeError('network'))
    .mockResolvedValueOnce(new Response('', { status: 404 }))
  const body = JSON.stringify({ change: { requestId: 'history-request' }, previewToken: 'preview' })
  await expect(apiRequest('/api/ledger/history/change', { method: 'POST', body })).rejects.toThrow('保存结果待核实')
  expect(readPendingMutations()[0]?.requestId).toBe('history-request')
})
