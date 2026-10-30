import { computed, readonly, ref } from 'vue'

export type ThemeMode = 'system' | 'light' | 'dark'
export type ResolvedTheme = 'light' | 'dark'

export const themeStorageKey = 'asset-keeper.theme'

const darkMediaQuery = '(prefers-color-scheme: dark)'
const mode = ref<ThemeMode>(readStoredMode())
const systemPrefersDark = ref(prefersDark())
export const resolvedTheme = computed<ResolvedTheme>(() =>
  mode.value === 'system' ? (systemPrefersDark.value ? 'dark' : 'light') : mode.value)

let mediaQuery: MediaQueryList | null = null
let listenerRegistered = false

function readStoredMode(): ThemeMode {
  if (typeof window === 'undefined') {
    return 'system'
  }

  try {
    const stored = window.localStorage.getItem(themeStorageKey)

    if (stored === 'light' || stored === 'dark' || stored === 'system') {
      return stored
    }
  } catch {
    // 浏览器存储不可用时按跟随系统处理。
  }

  return 'system'
}

function prefersDark(): boolean {
  if (typeof window === 'undefined') {
    return false
  }

  return !!window.matchMedia?.(darkMediaQuery)?.matches
}

function applyTheme(): void {
  if (typeof document === 'undefined') {
    return
  }

  const resolved = resolvedTheme.value
  const root = document.documentElement
  root.dataset.colorScheme = resolved
  root.style.colorScheme = resolved

  const themeColor = resolved === 'dark' ? '#0B0C0F' : '#FFFFFF'
  document.querySelectorAll<HTMLMetaElement>('meta[name="theme-color"]').forEach((meta) => {
    meta.removeAttribute('media')
    meta.setAttribute('content', themeColor)
  })
}

export function initializeTheme(): void {
  mode.value = readStoredMode()

  if (typeof window !== 'undefined' && !listenerRegistered) {
    listenerRegistered = true
    mediaQuery = window.matchMedia?.(darkMediaQuery) ?? null
    systemPrefersDark.value = mediaQuery?.matches ?? false
    mediaQuery?.addEventListener?.('change', (event) => {
      systemPrefersDark.value = event.matches

      if (mode.value === 'system') {
        applyTheme()
      }
    })
  } else {
    systemPrefersDark.value = prefersDark()
  }

  applyTheme()
}

export function setThemeMode(next: ThemeMode): void {
  mode.value = next

  if (next === 'system') {
    systemPrefersDark.value = prefersDark()
  }

  try {
    window.localStorage.setItem(themeStorageKey, next)
  } catch {
    // 存储失败时仍然应用本次选择，只是刷新后回到跟随系统。
  }

  applyTheme()
}

export function useTheme() {
  return {
    mode: readonly(mode),
    resolvedTheme,
    setThemeMode
  }
}
