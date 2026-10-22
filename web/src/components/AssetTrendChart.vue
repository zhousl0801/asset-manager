<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import * as echarts from 'echarts/core'
import { LineChart } from 'echarts/charts'
import { DataZoomComponent, GridComponent, TooltipComponent } from 'echarts/components'
import { CanvasRenderer } from 'echarts/renderers'
import type { EChartsOption } from 'echarts'
import { buildTrendOption } from '../utils/trend'
import type { AssetTrend } from '../types/trend'

echarts.use([LineChart, GridComponent, TooltipComponent, DataZoomComponent, CanvasRenderer])

const props = defineProps<{
  trend: AssetTrend
}>()

const container = ref<HTMLElement | null>(null)
let chart: echarts.ECharts | null = null

function resize(): void {
  chart?.resize()
}

onMounted(() => {
  if (!container.value) {
    return
  }

  chart = echarts.init(container.value, undefined, { renderer: 'canvas' })
  chart.setOption(buildTrendOption(props.trend) as EChartsOption)
  window.addEventListener('resize', resize)
})

watch(
  () => props.trend,
  (trend) => {
    chart?.setOption(buildTrendOption(trend) as EChartsOption, { notMerge: true })
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
  <div ref="container" class="trend-chart" role="img" aria-label="总资产趋势图" />
</template>

<style scoped>
.trend-chart {
  width: 100%;
  height: 220px;
}
</style>
