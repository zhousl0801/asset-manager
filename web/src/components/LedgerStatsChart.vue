<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import * as echarts from 'echarts/core'
import { BarChart } from 'echarts/charts'
import { GridComponent, TooltipComponent } from 'echarts/components'
import { CanvasRenderer } from 'echarts/renderers'
import type { EChartsOption } from 'echarts'
import { formatMoney } from '../utils/format'
import { chartTheme } from '../utils/chartTheme'
import type { EntryKind, EntryPeriod, EntryStatsPoint } from '../types/entry'

echarts.use([BarChart, GridComponent, TooltipComponent, CanvasRenderer])

const props = defineProps<{
  points: EntryStatsPoint[]
  direction: EntryKind
  period: EntryPeriod
  total: number
}>()

const container = ref<HTMLElement | null>(null)
let chart: echarts.ECharts | null = null

const chartColors: Record<EntryKind, string> = {
  expense: chartTheme.brand,
  income: chartTheme.loss
}

function buildOption(): EChartsOption {
  const values = props.points.map((point) => point[props.direction])
  const color = chartColors[props.direction]

  return {
    animation: false,
    grid: { left: 46, right: 10, top: 12, bottom: 24 },
    tooltip: {
      trigger: 'axis',
      confine: true,
      axisPointer: { type: 'shadow', shadowStyle: { color: chartTheme.pointerShadow } },
      formatter: (params: unknown) => {
        const first = Array.isArray(params) ? params[0] as { dataIndex?: number } | undefined : undefined
        const index = first?.dataIndex
        const point = index === undefined ? undefined : props.points[index]

        if (!point) {
          return ''
        }

        return `${point.label}<br/>${props.direction === 'income' ? '收入' : '支出'} ¥${formatMoney(point[props.direction])}`
      }
    },
    xAxis: {
      type: 'category',
      data: props.points.map((point) => point.label),
      axisTick: { show: false },
      axisLine: { lineStyle: { color: chartTheme.axis } },
      axisLabel: {
        color: chartTheme.text,
        fontSize: 12,
        interval: props.period === 'month' ? 4 : 0
      }
    },
    yAxis: {
      type: 'value',
      min: 0,
      axisLabel: {
        color: chartTheme.text,
        fontSize: 12,
        formatter: (value: number) => value >= 10000 ? `${(value / 10000).toFixed(1)}万` : String(value)
      },
      splitLine: { lineStyle: { color: chartTheme.grid } }
    },
    series: [
      {
        name: props.direction === 'income' ? '收入' : '支出',
        type: 'bar',
        data: values,
        barMaxWidth: props.period === 'year' ? 16 : 8,
        barCategoryGap: '35%',
        itemStyle: {
          color,
          borderRadius: [4, 4, 0, 0]
        },
        emphasis: {
          itemStyle: { color }
        }
      }
    ]
  }
}

function render(): void {
  chart?.setOption(buildOption(), { notMerge: true })
}

function resize(): void {
  chart?.resize()
}

onMounted(() => {
  if (!container.value) {
    return
  }

  chart = echarts.init(container.value, undefined, { renderer: 'canvas' })
  render()
  window.addEventListener('resize', resize)
})

watch(() => [props.points, props.direction, props.period], render, { deep: true })

onBeforeUnmount(() => {
  window.removeEventListener('resize', resize)
  chart?.dispose()
  chart = null
})
</script>

<template>
  <div
    ref="container"
    class="stats-chart"
    role="img"
    :aria-label="`${direction === 'income' ? '收入' : '支出'}趋势图，合计 ${formatMoney(total)} 元`"
  />
</template>

<style scoped>
.stats-chart {
  width: 100%;
  height: 184px;
}
</style>
