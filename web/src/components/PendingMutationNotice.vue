<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { getApiErrorMessage, pendingMutationCount, readPendingMutations, recoverMutation } from '../api/client'

const isChecking = ref(false)
const message = ref('')
const hasPending = computed(() => pendingMutationCount.value > 0)

function reloadPending(): void {
  readPendingMutations()
}

onMounted(() => {
  reloadPending()
  window.addEventListener('storage', reloadPending)
})
onBeforeUnmount(() => window.removeEventListener('storage', reloadPending))

async function check(retry: boolean): Promise<void> {
  if (isChecking.value) return
  isChecking.value = true
  message.value = ''

  try {
    for (const request of readPendingMutations()) {
      await recoverMutation(request, retry)
    }
    window.location.reload()
  } catch (error) {
    message.value = getApiErrorMessage(error, '暂时无法核实，请联网后重试。')
  } finally {
    isChecking.value = false
  }
}
</script>

<template>
  <aside v-if="hasPending || message" class="pending-notice" role="status">
    <p>有保存结果待核实，原请求已保留。请先确认，避免重复记账。</p>
    <p v-if="message">{{ message }}</p>
    <button type="button" :disabled="isChecking" @click="check(false)">查询保存结果</button>
    <button type="button" :disabled="isChecking" @click="check(true)">重试原请求</button>
  </aside>
</template>

<style scoped>
.pending-notice {
  position: relative;
  z-index: 60;
  max-width: 430px;
  box-sizing: border-box;
  margin: 0 auto;
  padding: 12px 16px;
  background: var(--color-bg-surface);
  color: var(--color-text-primary);
  border-bottom: 1px solid var(--color-border-default);
  font-size: 12px;
}
.pending-notice button {
  min-height: 40px;
  margin-right: 12px;
  color: var(--color-brand);
}
</style>
