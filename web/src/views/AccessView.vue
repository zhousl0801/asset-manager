<script setup lang="ts">
import { ref } from 'vue'
import { login } from '../api/access'

const password = ref('')
const error = ref('')
const busy = ref(false)

async function submit(): Promise<void> {
  if (busy.value || !password.value) return
  busy.value = true
  error.value = ''
  try {
    await login(password.value)
    password.value = ''
  } catch (value) {
    error.value = value instanceof Error ? value.message : '验证失败，请重试'
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <main class="access-page">
    <form class="access-card" @submit.prevent="submit">
      <div class="access-mark">资</div>
      <h1>资产管家</h1>
      <p>输入个人访问口令，继续查看账本</p>
      <label for="access-password">访问口令</label>
      <input
        id="access-password"
        v-model="password"
        type="password"
        autocomplete="current-password"
        placeholder="请输入访问口令"
        autofocus
      >
      <p v-if="error" class="access-error" role="alert">{{ error }}</p>
      <button class="access-submit" type="submit" :disabled="busy || !password">
        {{ busy ? '验证中…' : '进入账本' }}
      </button>
    </form>
  </main>
</template>

<style scoped>
.access-page {
  display: grid;
  min-height: 100dvh;
  place-items: center;
  padding: 24px;
  box-sizing: border-box;
}
.access-card {
  display: grid;
  width: min(100%, 360px);
  gap: 14px;
  padding: 30px 24px;
  border: 1px solid var(--color-border-subtle);
  border-radius: 18px;
  background: var(--color-bg-surface);
  box-shadow: 0 12px 36px rgb(30 50 40 / 8%);
}
.access-mark {
  display: grid;
  width: 44px;
  height: 44px;
  place-items: center;
  border-radius: 12px;
  background: var(--color-brand-soft);
  color: var(--color-brand);
  font-size: 20px;
  font-weight: 700;
}
h1 { margin: 4px 0 0; font-size: 22px; }
.access-card > p { margin: 0; color: var(--color-text-secondary); font-size: 13px; }
label { margin-top: 10px; font-size: 13px; font-weight: 600; }
input {
  box-sizing: border-box;
  width: 100%;
  min-height: 48px;
  padding: 0 13px;
  border: 1px solid var(--color-border-control);
  border-radius: 9px;
  background: var(--color-bg-page);
}
.access-card .access-error { color: #ad3c32; }
.access-submit {
  min-height: 48px;
  margin-top: 4px;
  border: 0;
  border-radius: 9px;
  background: var(--color-brand);
  color: white;
  font-weight: 650;
}
.access-submit:disabled { opacity: .6; }
</style>
