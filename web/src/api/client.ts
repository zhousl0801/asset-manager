import { ref } from 'vue'
import { accessAuthorization } from './access'

export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}

interface ProblemDetails {
  title?: string
  detail?: string
  errors?: Record<string, string[]>
}

export async function readOnlyRequest<T>(path: string, init?: RequestInit): Promise<T> {
  const baseUrl = import.meta.env.VITE_API_BASE_URL ?? ''
  const headers = new Headers(init?.headers)
  const authorization = accessAuthorization()
  if (authorization && !headers.has('Authorization')) {
    headers.set('Authorization', authorization)
  }

  if (!headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json')
  }

  const response = await fetch(`${baseUrl}${path}`, {
    ...init,
    headers
  })

  if (!response.ok) {
    if (response.status === 401) {
      const { clearAccess } = await import('./access')
      clearAccess()
    }
    const problem = await readProblem(response)
    const firstFieldError = problem?.errors
      ? Object.values(problem.errors).flat()[0]
      : undefined
    const message = firstFieldError
      ?? problem?.detail
      ?? problem?.title
      ?? `请求失败（HTTP ${response.status}）`

    throw new ApiError(response.status, message)
  }

  if (response.status === 204) {
    return undefined as T
  }

  return (await response.json()) as T
}

const PendingPrefix = 'asset-keeper.pending.'

export interface PendingMutation {
  requestId: string
  path: string
  method: string
  body: string
}

export const pendingMutationCount = ref(0)

export function readPendingMutations(): PendingMutation[] {
  const result: PendingMutation[] = []

  for (let index = 0; index < localStorage.length; index++) {
    const key = localStorage.key(index)

    if (key?.startsWith(PendingPrefix)) {
      result.push(JSON.parse(localStorage.getItem(key)!) as PendingMutation)
    }
  }

  pendingMutationCount.value = result.length
  return result
}

function remember(request: PendingMutation): void {
  try {
    localStorage.setItem(`${PendingPrefix}${request.requestId}`, JSON.stringify(request))
    readPendingMutations()
  } catch {
    throw new ApiError(0, '无法保存待核实记录，请检查浏览器存储后重试。')
  }
}

function forget(requestId: string): void {
  localStorage.removeItem(`${PendingPrefix}${requestId}`)
  readPendingMutations()
}

export async function lookupMutation<T>(requestId: string): Promise<T> {
  const receipt = await readOnlyRequest<{ result: T }>(`/api/ledger/requests/${requestId}`)
  return receipt.result
}

export async function recoverMutation(request: PendingMutation, retry: boolean): Promise<unknown> {
  try {
    const result = await lookupMutation(request.requestId)
    forget(request.requestId)
    return result
  } catch (error) {
    if (retry && error instanceof ApiError && error.status === 404) {
      return apiRequest(request.path, { method: request.method, body: request.body })
    }

    throw error
  }
}

export async function apiRequest<T>(path: string, init?: RequestInit): Promise<T> {
  const body = typeof init?.body === 'string' ? init.body : undefined
  const method = init?.method?.toUpperCase() ?? 'GET'
  const parsed = body ? JSON.parse(body) as { requestId?: string; change?: { requestId?: string } } : undefined
  const requestId = method !== 'GET' ? (parsed?.requestId ?? parsed?.change?.requestId) : undefined

  if (!requestId) {
    return readOnlyRequest<T>(path, init)
  }

  const pending = readPendingMutations()
  const original = pending.find((request) => request.requestId === requestId)

  if (pending.some((request) => request.requestId !== requestId)
    || (original && (original.path !== path || original.body !== body || original.method !== method))) {
    throw new ApiError(0, '上次保存结果待核实，请先在顶部查询结果或重试原请求。')
  }

  remember({ requestId, path, method, body: body! })

  try {
    const result = await readOnlyRequest<T>(path, init)
    forget(requestId)
    return result
  } catch (error) {
    if (error instanceof ApiError && error.status >= 400 && error.status < 500) {
      forget(requestId)
      throw error
    }

    try {
      const result = await lookupMutation<T>(requestId)
      forget(requestId)
      return result
    } catch {
      throw new ApiError(0, '保存结果待核实，原内容已保留；请查询结果或重试原请求，勿重新记账。')
    }
  }
}

export function getApiErrorMessage(error: unknown, fallback: string): string {
  if (error instanceof ApiError) {
    return error.message
  }

  return fallback
}

async function readProblem(response: Response): Promise<ProblemDetails | null> {
  try {
    return (await response.json()) as ProblemDetails
  } catch {
    return null
  }
}
