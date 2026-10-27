import {
  browserSupportsWebAuthn,
  startAuthentication,
  startRegistration,
  type AuthenticationResponseJSON,
  type PublicKeyCredentialCreationOptionsJSON,
  type PublicKeyCredentialRequestOptionsJSON,
  type RegistrationResponseJSON
} from '@simplewebauthn/browser'
import { hasAccess } from './access'
import { readOnlyRequest } from './client'

export const passkeySupported = browserSupportsWebAuthn()

export interface PasskeySummary {
  id: string
  label: string
  createdAt: string
  lastUsedAt: string | null
}

function describeError(error: unknown, fallback: string): string {
  const name = error instanceof Error ? error.name : ''

  switch (name) {
    case 'NotAllowedError':
    case 'AbortError':
      return '已取消指纹 / Face ID 验证'
    case 'InvalidStateError':
      return '此设备已注册过 Passkey，可直接用它登录'
    case 'SecurityError':
      return '当前域名与 Passkey 配置不一致，请检查部署地址'
    case 'NotSupportedError':
      return '当前浏览器不支持 Passkey，请改用访问口令'
    default:
      return fallback
  }
}

export async function registerPasskey(label?: string): Promise<PasskeySummary> {
  if (!passkeySupported) {
    throw new Error('当前浏览器不支持 Passkey，请改用访问口令')
  }

  const options = await readOnlyRequest<PublicKeyCredentialCreationOptionsJSON>(
    '/api/access/passkeys/registration/options',
    { method: 'POST' })

  let response: RegistrationResponseJSON
  try {
    response = await startRegistration({ optionsJSON: options })
  } catch (error) {
    throw new Error(describeError(error, '启用指纹 / Face ID 未完成，请重试'))
  }

  return await readOnlyRequest<PasskeySummary>('/api/access/passkeys/registration/complete', {
    method: 'POST',
    body: JSON.stringify({ response, label })
  })
}

export async function signInWithPasskey(): Promise<void> {
  if (!passkeySupported) {
    throw new Error('当前浏览器不支持 Passkey，请改用访问口令')
  }

  const options = await readOnlyRequest<PublicKeyCredentialRequestOptionsJSON>(
    '/api/access/passkeys/login/options',
    { method: 'POST' })

  let response: AuthenticationResponseJSON
  try {
    response = await startAuthentication({ optionsJSON: options })
  } catch (error) {
    throw new Error(describeError(error, '指纹 / Face ID 登录未完成，请重试'))
  }

  await readOnlyRequest('/api/access/passkeys/login/complete', {
    method: 'POST',
    body: JSON.stringify({ response })
  })

  hasAccess.value = true
}

export async function fetchPasskeys(): Promise<PasskeySummary[]> {
  return await readOnlyRequest<PasskeySummary[]>('/api/access/passkeys')
}

export async function deletePasskey(id: string): Promise<void> {
  await readOnlyRequest<void>(`/api/access/passkeys/${id}`, { method: 'DELETE' })
}
