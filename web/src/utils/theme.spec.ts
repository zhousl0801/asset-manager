import { beforeEach, expect, test, vi } from 'vitest'
import {
  initializeTheme,
  resolvedTheme,
  setThemeMode,
  themeStorageKey
} from './theme'

let systemChangeHandler: ((event: { matches: boolean }) => void) | null = null
const media = {
  matches: false,
  media: '(prefers-color-scheme: dark)',
  onchange: null,
  addEventListener: (_type: string, handler: (event: { matches: boolean }) => void) => {
    systemChangeHandler = handler
  },
  removeEventListener: vi.fn(),
  dispatchEvent: vi.fn()
}

beforeEach(() => {
  localStorage.clear()
  media.matches = false
  vi.stubGlobal('matchMedia', vi.fn(() => media))
  document.documentElement.removeAttribute('data-color-scheme')
  document.documentElement.style.colorScheme = ''
  initializeTheme()
  setThemeMode('system')
})

test('defaults to the system theme and applies the resolved scheme', () => {
  expect(resolvedTheme.value).toBe('light')
  expect(document.documentElement.dataset.colorScheme).toBe('light')
  expect(document.documentElement.style.colorScheme).toBe('light')
})

test('an explicit dark choice overrides a light system and persists', () => {
  setThemeMode('dark')

  expect(resolvedTheme.value).toBe('dark')
  expect(document.documentElement.dataset.colorScheme).toBe('dark')
  expect(localStorage.getItem(themeStorageKey)).toBe('dark')
})

test('an explicit light choice overrides a dark system', () => {
  media.matches = true
  initializeTheme()

  expect(resolvedTheme.value).toBe('dark')

  setThemeMode('light')

  expect(resolvedTheme.value).toBe('light')
  expect(document.documentElement.dataset.colorScheme).toBe('light')
})

test('system mode follows device changes while an explicit choice ignores them', () => {
  media.matches = true
  systemChangeHandler?.({ matches: true })
  expect(resolvedTheme.value).toBe('dark')

  setThemeMode('light')
  systemChangeHandler?.({ matches: true })

  expect(resolvedTheme.value).toBe('light')
})

test('initialization reads a stored explicit choice', () => {
  localStorage.setItem(themeStorageKey, 'dark')

  initializeTheme()

  expect(resolvedTheme.value).toBe('dark')
  expect(document.documentElement.dataset.colorScheme).toBe('dark')
})
