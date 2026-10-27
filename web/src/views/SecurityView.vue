<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { logout, passkeyEnabled } from '../api/access'
import { deletePasskey, fetchPasskeys, passkeySupported, registerPasskey, type PasskeySummary } from '../api/passkeys'
import PageHeader from '../components/PageHeader.vue'

const router = useRouter()
const credentials = ref<PasskeySummary[]>([])
const loading = ref(false)
const busy = ref(false)
const error = ref('')
const notice = ref('')

function goBack(): void {
  void router.push({ name: 'mine' })
}

function deviceLabel(): string {
  const agent = navigator.userAgent

  if (/iPhone|iPad|iPod/.test(agent)) return 'iPhone / iPad'
  if (/Android/.test(agent)) return 'Android 设备'
  if (/Macintosh/.test(agent)) return 'Mac'
  if (/Windows/.test(agent)) return 'Windows'

  return '此浏览器'
}

function formatDate(value: string | null): string {
  if (!value) return '—'
  return value.slice(0, 10)
}

async function load(): Promise<void> {
  if (!passkeyEnabled.value) return

  loading.value = true
  error.value = ''

  try {
    credentials.value = await fetchPasskeys()
  } catch (value) {
    error.value = value instanceof Error ? value.message : '读取 Passkey 失败，请重试'
  } finally {
    loading.value = false
  }
}

async function enable(): Promise<void> {
  if (busy.value) return

  busy.value = true
  error.value = ''
  notice.value = ''

  try {
    const created = await registerPasskey(deviceLabel())
    notice.value = `已在此设备启用指纹 / Face ID 登录（${created.label}）。`
    await load()
  } catch (value) {
    error.value = value instanceof Error ? value.message : '启用失败，请重试'
  } finally {
    busy.value = false
  }
}

async function remove(credential: PasskeySummary): Promise<void> {
  if (busy.value) return
  if (!window.confirm(`删除「${credential.label}」的 Passkey？删除后该设备需改用访问口令登录。`)) return

  busy.value = true
  error.value = ''
  notice.value = ''

  try {
    await deletePasskey(credential.id)
    notice.value = `已删除「${credential.label}」的 Passkey。`
    await load()
  } catch (value) {
    error.value = value instanceof Error ? value.message : '删除失败，请重试'
  } finally {
    busy.value = false
  }
}

async function signOut(): Promise<void> {
  if (busy.value) return
  busy.value = true
  await logout()
}

onMounted(load)
</script>

<template>
  <main class="page security-page">
    <PageHeader title="登录与安全" back @back="goBack" />

    <section class="security-card">
      <h2>指纹 / Face ID 登录</h2>
      <p>
        启用后，登录页可直接用本机的指纹、Face ID 或系统密码进入，不必再输入访问口令。
        换设备或清除浏览器数据后，重新启用一次即可。
      </p>
      <p v-if="!passkeyEnabled" class="security-note">
        服务器尚未启用 Passkey（需要配置 Passkey:RpId 与 Passkey:Origins），当前只能使用访问口令。
      </p>
      <p v-else-if="!passkeySupported" class="security-note">
        当前浏览器不支持 Passkey，请使用访问口令登录，或改在手机 Safari / Chrome 上启用。
      </p>
      <button v-else class="primary-button security-action" type="button" :disabled="busy" @click="enable">
        {{ busy ? '等待验证…' : '在此设备启用' }}
      </button>
    </section>

    <p v-if="notice" class="security-notice" role="status">{{ notice }}</p>
    <p v-if="error" class="security-error" role="alert">{{ error }}</p>

    <section v-if="passkeyEnabled" class="security-card">
      <h2>已注册的 Passkey</h2>
      <p v-if="loading" class="security-note">正在读取…</p>
      <p v-else-if="!credentials.length" class="security-note">还没有注册 Passkey，启用后即可免口令登录。</p>
      <ul v-else class="passkey-list">
        <li v-for="credential in credentials" :key="credential.id">
          <div>
            <strong>{{ credential.label }}</strong>
            <small>创建于 {{ formatDate(credential.createdAt) }} · 最近使用 {{ formatDate(credential.lastUsedAt) }}</small>
          </div>
          <button class="text-button danger" type="button" :disabled="busy" @click="remove(credential)">删除</button>
        </li>
      </ul>
    </section>

    <section class="security-card">
      <h2>退出登录</h2>
      <p>退出后需要重新验证访问口令或指纹 / Face ID，已保存的 Passkey 不会删除。</p>
      <button class="secondary-button security-action" type="button" :disabled="busy" @click="signOut">
        退出登录
      </button>
    </section>

    <p class="timeline-note">
      Passkey 只用于验证本人身份，不参与账本数据；删除全部 Passkey 后仍可用访问口令登录。
    </p>
  </main>
</template>

<style scoped>
.security-page {
  padding-bottom: 40px;
}
.security-card {
  display: grid;
  gap: 10px;
  padding: 16px 14px;
  border: 1px solid var(--color-border-subtle);
  border-radius: 12px;
  background: var(--color-bg-surface);
}
.security-card + .security-card {
  margin-top: 12px;
}
.security-card h2 {
  margin: 0;
  font-size: 14px;
  font-weight: 650;
}
.security-card p {
  margin: 0;
  color: var(--color-text-secondary);
  font-size: 11px;
  line-height: 1.7;
}
.security-action {
  justify-self: start;
  min-height: 38px;
  padding: 0 14px;
}
.security-notice,
.security-error {
  margin: 12px 0 0;
  font-size: 11px;
  line-height: 1.7;
}
.security-notice { color: var(--color-brand); }
.security-error { color: #ad3c32; }
.security-note { color: var(--color-text-secondary); }
.passkey-list {
  display: grid;
  gap: 2px;
  margin: 0;
  padding: 0;
  list-style: none;
}
.passkey-list li {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 10px;
  padding: 10px 0;
  border-top: 1px solid var(--color-border-subtle);
}
.passkey-list li:first-child { border-top: 0; }
.passkey-list strong {
  display: block;
  font-size: 13px;
  font-weight: 600;
}
.passkey-list small {
  display: block;
  margin-top: 3px;
  color: var(--color-text-secondary);
  font-size: 10px;
}
</style>
