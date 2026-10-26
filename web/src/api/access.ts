import { ref } from 'vue'

const AccessTokenKey = 'asset-keeper.access-token'
const baseUrl = import.meta.env.VITE_API_BASE_URL ?? ''

export const hasAccess = ref(Boolean(sessionStorage.getItem(AccessTokenKey)))

export async function verifyAccessPassword(password: string): Promise<void> {
  const response = await fetch(`${baseUrl}/api/access/verify`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ password })
  })

  if (!response.ok) {
    throw new Error(response.status === 401 ? '访问口令不正确' : '暂时无法验证，请稍后重试')
  }

  sessionStorage.setItem(AccessTokenKey, password)
  hasAccess.value = true
}

export function clearAccess(): void {
  sessionStorage.removeItem(AccessTokenKey)
  hasAccess.value = false
}

export function accessAuthorization(): string | null {
  const token = sessionStorage.getItem(AccessTokenKey)
  return token ? `Bearer ${token}` : null
}
