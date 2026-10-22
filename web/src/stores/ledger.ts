import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { ApiError, getApiErrorMessage } from '../api/client'
import { fetchLedger, initializeLedger } from '../api/ledger'
import type { InitializeLedgerInput, LedgerOverview } from '../types/ledger'

export type LedgerLoadStatus = 'ready' | 'missing' | 'error'

export const useLedgerStore = defineStore('ledger', () => {
  const overview = ref<LedgerOverview | null>(null)
  const isLoading = ref(false)
  const isSaving = ref(false)
  const errorMessage = ref<string | null>(null)
  const lastLoadStatus = ref<LedgerLoadStatus | null>(null)
  const isInitialized = computed(() => overview.value !== null)

  async function load(): Promise<LedgerLoadStatus> {
    isLoading.value = true
    errorMessage.value = null

    try {
      overview.value = await fetchLedger()
      lastLoadStatus.value = 'ready'
      return 'ready'
    } catch (error) {
      overview.value = null

      if (error instanceof ApiError && error.status === 404) {
        lastLoadStatus.value = 'missing'
        return 'missing'
      }

      errorMessage.value = getApiErrorMessage(error, '无法连接服务器，请检查网络或后端服务。')
      lastLoadStatus.value = 'error'
      return 'error'
    } finally {
      isLoading.value = false
    }
  }

  async function initialize(input: InitializeLedgerInput): Promise<LedgerOverview> {
    isSaving.value = true

    try {
      const result = await initializeLedger(input)
      overview.value = result
      return result
    } finally {
      isSaving.value = false
    }
  }

  return {
    overview,
    isLoading,
    isSaving,
    errorMessage,
    lastLoadStatus,
    isInitialized,
    load,
    initialize
  }
})
