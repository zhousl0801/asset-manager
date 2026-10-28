/**
 * ECharts 不能直接读取 CSS 变量，图表色集中在这里维护。
 * 浅色 / 深色图表的色值与 docs/design/ui-color-tokens.css 的语义值对应。
 */
const light = {
  brand: '#002FA7',
  gain: '#B93838',
  loss: '#18734E',
  text: '#626C80',
  axis: '#E2E6EE',
  grid: '#EEF0F6',
  pointer: '#9AA1B4',
  marker: '#5C6272',
  areaStrong: 'rgba(0, 47, 167, 0.16)',
  areaSoft: 'rgba(0, 47, 167, 0.01)',
  pointerShadow: 'rgba(24, 32, 51, 0.05)'
} as const

const dark = {
  brand: '#8FAAFF',
  gain: '#FF6B6B',
  loss: '#4CD97B',
  text: '#A1A1A6',
  axis: 'rgba(84, 84, 88, 0.55)',
  grid: 'rgba(255, 255, 255, 0.08)',
  pointer: '#8E8E93',
  marker: '#A1A1A6',
  areaStrong: 'rgba(143, 170, 255, 0.24)',
  areaSoft: 'rgba(143, 170, 255, 0.02)',
  pointerShadow: 'rgba(255, 255, 255, 0.04)'
} as const

export function chartTheme() {
  return typeof window !== 'undefined'
    && window.matchMedia?.('(prefers-color-scheme: dark)').matches
    ? dark
    : light
}

/** 跟随浏览器根字号缩放图表文字，接近 Dynamic Type 的效果。 */
export function chartFontSize(px = 12): number {
  if (typeof window === 'undefined') {
    return px
  }

  const root = Number.parseFloat(getComputedStyle(document.documentElement).fontSize)
  return root > 0 ? Math.round(px * root / 16) : px
}
