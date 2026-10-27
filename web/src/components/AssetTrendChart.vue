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
import { buildScopedTrend, buildTrendOption } from '../utils/trend'
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
let chart: echarts.ECharts | null = null

function resize(): void {
  chart?.resize()
}

onMounted(() => {
  if (!container.value) {
    return
  }

  chart = echarts.init(container.value, undefined, { renderer: 'canvas' })
  chart.setOption(buildTrendOption(props.trend, scoped.value) as EChartsOption)
  chart.on('updateAxisPointer', (event: unknown) => {
    const payload = event as { axesInfo?: { axisDim: string; value: number | string }[] }
    const value = payload.axesInfo?.find((axis) => axis.axisDim === 'x')?.value
    const point = typeof value === 'number' && Number.isInteger(value)
      ? props.trend.points[value]
      : props.trend.points.find((item) => item.date === value)

    if (point) emit('select', point)
  })
  window.addEventListener('resize', resize)
})

watch(
  () => props.trend,
  (trend) => {
    chart?.setOption(buildTrendOption(trend, buildScopedTrend(trend)) as EChartsOption, { notMerge: true })
  },
  { deep: true }
)

onBeforeUnmount(() => {
  window.removeEventListener('resize', resize)
  chart?.dispose()
  chart = null
})
</script>

<template>
  <div ref="container" class="trend-chart" role="img" aria-label="资产趋势图" />
</template>

<style scoped>
.trend-chart {
  width: 100%;
  height: 120px;
}
</style>
