<script setup lang="ts">
import { storeToRefs } from 'pinia'
import { onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { getApiErrorMessage } from '../api/client'
import { useLedgerStore } from '../stores/ledger'
import { MaximumAmount, parseAmount, todayInBusinessTimeZone } from '../utils/format'

const router = useRouter()
const ledgerStore = useLedgerStore()
const { isSaving } = storeToRefs(ledgerStore)

const today = todayInBusinessTimeZone()
const isChecking = ref(true)
const loadError = ref('')
const form = reactive({
  startDate: today,
  cashAsOfDate: today,
  cashBalance: ''
})
const errors = reactive({
  startDate: '',
  cashBalance: '',
  form: ''
})

onMounted(checkLedger)

async function checkLedger(): Promise<void> {
  if (ledgerStore.isInitialized) {
    await router.replace({ name: 'home' })
    return
  }

  if (ledgerStore.lastLoadStatus === 'missing') {
    isChecking.value = false
    return
  }

  isChecking.value = true
  loadError.value = ''
  const status = await ledgerStore.load()
  isChecking.value = false

  if (status === 'ready') {
    await router.replace({ name: 'home' })
    return
  }

  if (status === 'error') {
    loadError.value = ledgerStore.errorMessage ?? '无法读取账本。'
  }
}

async function submit(): Promise<void> {
  clearErrors()

  if (!form.startDate) {
    errors.startDate = '请选择起始日期。'
    return
  }

  if (form.startDate > today) {
    errors.startDate = '起始日期不能晚于今天。'
    return
  }

  if (!form.cashAsOfDate || form.cashAsOfDate < form.startDate || form.cashAsOfDate > today) {
    errors.form = '现金余额日期须在账本起始日期与今天之间。'
    return
  }

  const amount = parseAmount(form.cashBalance)

  if (amount === null) {
    errors.cashBalance = `请输入不小于 0 的金额，最多两位小数，且不超过 ${MaximumAmount.toLocaleString('zh-CN')}。`
    return
  }

  try {
    await ledgerStore.initialize({
      startDate: form.startDate,
      cashAsOfDate: form.cashAsOfDate,
      cashBalance: amount
    })
    await router.replace({ name: 'home' })
  } catch (error) {
    errors.form = getApiErrorMessage(error, '保存失败，请检查网络后重试。')
  }
}

function clearErrors(): void {
  errors.startDate = ''
  errors.cashBalance = ''
  errors.form = ''
}
</script>

<template>
  <main class="page">
    <header class="app-header">
      <div>
        <p class="app-name">资产管家</p>
        <p class="app-sub">建立起始账本</p>
      </div>
      <span class="currency-tag">CNY</span>
    </header>

    <p v-if="isChecking" class="state">正在检查账本…</p>

    <section v-else-if="loadError" class="state">
      <p>{{ loadError }}</p>
      <button class="secondary-button" type="button" @click="checkLedger">重新检查</button>
    </section>

    <template v-else>
      <section class="intro">
        <h1>设置起始资产</h1>
        <p>起始日期和现金余额只建立统计起点，不计为收入或投资收益。其他资产将在对应页面录入。</p>
      </section>

      <form class="setup-form" novalidate @submit.prevent="submit">
        <label class="field"><span>现金余额日期</span><input v-model="form.cashAsOfDate" type="date" :min="form.startDate" :max="today" required /><small>所填现金余额在这一天成立，独立于账本起始日期。</small></label>
        <label class="field">
          <span>账本起始日期</span>
          <input v-model="form.startDate" type="date" :max="today" required />
          <small v-if="errors.startDate" class="field-error">{{ errors.startDate }}</small>
        </label>

        <label class="field">
          <span>起始现金余额（元）</span>
          <input
            v-model="form.cashBalance"
            type="text"
            inputmode="decimal"
            autocomplete="off"
            placeholder="0.00"
            required
          />
          <small class="field-hint">统一现金池的当前余额，写入后作为账本起点。</small>
          <small v-if="errors.cashBalance" class="field-error">{{ errors.cashBalance }}</small>
        </label>

        <p v-if="errors.form" class="form-error" role="alert">{{ errors.form }}</p>

        <button class="primary-button" type="submit" :disabled="isSaving">
          {{ isSaving ? '保存中…' : '建立账本' }}
        </button>
      </form>
    </template>
  </main>
</template>

<style scoped>
.intro h1 {
  margin: 0 0 10px;
  font-size: 1.625rem;
  font-weight: 600;
  letter-spacing: -0.6px;
}

.intro p {
  margin: 0 0 28px;
  color: var(--color-text-secondary);
  font-size: 0.75rem;
  line-height: 1.8;
}

.setup-form {
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 8px;
  color: var(--color-text-secondary);
  font-size: 0.75rem;
}

.field input {
  box-sizing: border-box;
  width: 100%;
  min-height: 48px;
  padding: 0 13px;
  border: 1px solid var(--color-border-control);
  border-radius: var(--radius-control);
  background: var(--color-bg-surface);
  color: var(--color-text-primary);
  font-size: 1rem;
}

.field input:hover {
  border-color: var(--color-border-hover);
}

.field input:focus-visible {
  border-color: var(--color-border-focus);
}

.field-hint {
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
  line-height: 1.7;
}

.field-error {
  color: var(--color-error);
  font-size: var(--text-footnote);
  line-height: 1.6;
}

.form-error {
  margin: 0;
  padding: 10px 12px;
  border-radius: var(--radius-tag);
  background: var(--color-error-bg);
  color: var(--color-error);
  font-size: 0.75rem;
  line-height: 1.7;
}
</style>
