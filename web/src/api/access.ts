import { ref } from 'vue'

const baseUrl = import.meta.env.VITE_API_BASE_URL ?? ''

export const hasAccess = ref(false)
export const accessReady = ref(false)
export const passkeyEnabled = ref(false)
export const passkeyAvailable = ref(false)

interface AccessSession {
  required: boolean
  authenticated: boolean
  passkeyEnabled?: boolean
  passkeyAvailable?: boolean
}

async function readError(response: Response, fallback: string): Promise<string> {
  try {
    const problem = await response.json() as { title?: string; detail?: string }
    return problem.detail ?? problem.title ?? fallback
  } catch {
    return fallback
  }
}

/** 询问服务端当前会话状态；开发环境未启用口令时直接放行。 */
export async function refreshAccess(): Promise<void> {
  try {
    const response = await fetch(`${baseUrl}/api/access/session`, {
      credentials: 'include',
      headers: { Accept: 'application/json' }
    })

    if (!response.ok) {
      throw new Error(`HTTP ${response.status}`)
    }

    const session = await response.json() as AccessSession
    hasAccess.value = !session.required || session.authenticated
    passkeyEnabled.value = session.passkeyEnabled === true
    passkeyAvailable.value = session.passkeyAvailable === true
  } catch {
    hasAccess.value = false
  } finally {
    accessReady.value = true
  }
}

export async function login(password: string): Promise<void> {
  const response = await fetch(`${baseUrl}/api/access/login`, {
    method: 'POST',
    credentials: 'include',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ password })
  })

  if (response.status === 401) {
    throw new Error('访问口令不正确')
  }

  if (response.status === 429) {
    throw new Error('尝试次数过多，请稍后再试')
  }

  if (!response.ok) {
    throw new Error(await readError(response, '暂时无法验证，请稍后重试'))
  }

  hasAccess.value = true
  accessReady.value = true
}

/** 服务端返回 401 时由请求层调用：会话已失效，回到口令页。 */
export function markAccessLost(): void {
  hasAccess.value = false
  accessReady.value = true
}

export async function logout(): Promise<void> {
  try {
    await fetch(`${baseUrl}/api/access/logout`, {
      method: 'POST',
      credentials: 'include'
    })
  } catch {
    // 网络失败时仍然在本地锁定；服务端会话到期后自然失效。
  } finally {
    markAccessLost()
  }
}
