/**
 * ECharts 不能直接读取 CSS 变量，图表色集中在这里维护。
 * 色值与 docs/design/ui-color-tokens.css 的语义 token 保持一一对应。
 */
export const chartTheme = {
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
