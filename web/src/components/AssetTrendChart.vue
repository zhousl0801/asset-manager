<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import * as echarts from 'echarts/core'
import { LineChart } from 'echarts/charts'
import {
  GridComponent,
  MarkLineComponent,
  MarkPointComponent,
  TooltipComponent
} from 'echarts/components'
import { CanvasRenderer } from 'echarts/renderers'
import type { EChartsOption } from 'echarts'
import { assetLabels, buildScopedTrend, buildTrendOption } from '../utils/trend'
import type { AssetTrend, AssetTrendPoint } from '../types/trend'

echarts.use([
  LineChart,
  GridComponent,
  TooltipComponent,
  MarkPointComponent,
  MarkLineComponent,
  CanvasRenderer
])

const props = defineProps<{
  trend: AssetTrend
}>()

const emit = defineEmits<{ select: [point: AssetTrendPoint] }>()

const container = ref<HTMLElement | null>(null)
const scoped = computed(() => buildScopedTrend(props.trend))
const chartLabel = computed(() => {
  const latest = scoped.value.latest
  const latestText = latest?.value == null ? '最新依据不足' : `最新同口径资产 ${latest.value.toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} 元`
  const types = scoped.value.baselineTypes.map((key) => assetLabels[key]).join('、')
  return `资产趋势图，${props.trend.from} 至 ${props.trend.to}；${latestText}${types ? `；包含${types}` : ''}`
})
let chart: echarts.ECharts | null = null
const colorScheme = typeof window !== 'undefined'
  ? window.matchMedia?.('(prefers-color-scheme: dark)')
  : undefined

function resize(): void {
  chart?.resize()
}

function render(): void {
  chart?.setOption(buildTrendOption(props.trend, scoped.value) as EChartsOption, { notMerge: true })
}

onMounted(() => {
  if (!container.value) {
    return
  }

  chart = echarts.init(container.value, undefined, { renderer: 'canvas' })
  render()
  chart.on('updateAxisPointer', (event: unknown) => {
    const payload = event as { axesInfo?: { axisDim: string; value: number | string }[] }
    const value = payload.axesInfo?.find((axis) => axis.axisDim === 'x')?.value
    const point = typeof value === 'number' && Number.isInteger(value)
      ? props.trend.points[value]
      : props.trend.points.find((item) => item.date === value)

    if (point) emit('select', point)
  })
  window.addEventListener('resize', resize)
  colorScheme?.addEventListener?.('change', render)
})

watch(
  () => props.trend,
  () => {
    render()
  },
  { deep: true }
)

onBeforeUnmount(() => {
  window.removeEventListener('resize', resize)
  colorScheme?.removeEventListener?.('change', render)
  chart?.dispose()
  chart = null
})
</script>

<template>
  <div ref="container" class="trend-chart" role="img" :aria-label="chartLabel" />
</template>

<style scoped>
.trend-chart {
  width: 100%;
  height: 120px;
}
</style>
