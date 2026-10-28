<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import * as echarts from 'echarts/core'
import { BarChart, LineChart } from 'echarts/charts'
import { DataZoomComponent, GridComponent, LegendComponent, TooltipComponent } from 'echarts/components'
import { CanvasRenderer } from 'echarts/renderers'
import type { EChartsOption } from 'echarts'
import { formatSignedMoney } from '../utils/format'
import { chartTheme } from '../utils/chartTheme'
import type { FundReturnPoint } from '../types/fundReturn'

echarts.use([BarChart, LineChart, GridComponent, TooltipComponent, LegendComponent, DataZoomComponent, CanvasRenderer])

const props = defineProps<{
  points: FundReturnPoint[]
  range: string
}>()

const container = ref<HTMLElement | null>(null)
let chart: echarts.ECharts | null = null

function buildOption(): EChartsOption {
  return {
    animation: false,
    grid: { left: 48, right: 14, top: 34, bottom: 26 },
    legend: {
      top: 0,
      right: 0,
      itemWidth: 10,
      itemHeight: 8,
      textStyle: { fontSize: 12, color: chartTheme.text }
    },
    tooltip: {
      trigger: 'axis',
      confine: true,
      formatter: (params: unknown) => {
        const items = Array.isArray(params) ? params as { dataIndex?: number }[] : []
        const index = items[0]?.dataIndex
        const point = index === undefined ? undefined : props.points[index]

        if (!point) {
          return ''
        }

        const daily = point.dailyProfit === null
          ? point.hasDisclosure ? '当日收益 —（依据不足）' : '当日收益 —（未披露）'
          : `当日收益 ${formatSignedMoney(point.dailyProfit)}`
        const cumulative = point.cumulativeProfit === null
          ? '累计收益 —'
          : `累计收益 ${formatSignedMoney(point.cumulativeProfit)}`
        const coverage = point.isComplete
          ? '全部基金已披露'
          : `覆盖 ${point.coveredPositions}/${point.totalPositions} 个持仓`

        return `${point.date}<br/>${daily}<br/>${cumulative}<br/>${coverage}`
      }
    },
    xAxis: {
      type: 'category',
      data: props.points.map((point) => point.date.slice(5).replace('-', '/')),
      axisTick: { show: false },
      axisLine: { lineStyle: { color: chartTheme.axis } },
      axisLabel: {
        color: chartTheme.text,
        fontSize: 12,
        interval: axisInterval()
      }
    },
    yAxis: {
      type: 'value',
      axisLabel: {
        color: chartTheme.text,
        fontSize: 12,
        formatter: (value: number) => Math.abs(value) >= 10000 ? `${(value / 10000).toFixed(1)}万` : String(value)
      },
      splitLine: { lineStyle: { color: chartTheme.grid } }
    },
    dataZoom: [{ type: 'inside' }],
    series: [
      {
        name: '当日收益',
        type: 'bar',
        data: props.points.map((point) => point.dailyProfit === null
          ? null
          : {
              value: point.dailyProfit,
              itemStyle: { color: point.dailyProfit >= 0 ? chartTheme.gain : chartTheme.loss }
            }),
        barMaxWidth: 8
      },
      {
        name: '累计收益',
        type: 'line',
        data: props.points.map((point) => point.cumulativeProfit),
        smooth: false,
        showSymbol: false,
        connectNulls: false,
        lineStyle: { width: 2, color: chartTheme.brand },
        itemStyle: { color: chartTheme.brand }
      }
    ]
  }
}

function axisInterval(): number {
  if (props.range === 'all' || props.points.length > 90) {
    return Math.ceil(props.points.length / 8)
  }

  return props.range === 'month' ? 3 : 6
}

function resize(): void {
  chart?.resize()
}

onMounted(() => {
  if (!container.value) {
    return
  }

  chart = echarts.init(container.value, undefined, { renderer: 'canvas' })
  chart.setOption(buildOption())
  window.addEventListener('resize', resize)
})

watch(
  () => [props.points, props.range],
  () => {
    chart?.setOption(buildOption(), { notMerge: true })
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
  <div ref="container" class="fund-return-chart" role="img" aria-label="投资收益走势图" />
</template>

<style scoped>
.fund-return-chart {
  width: 100%;
  height: 220px;
}
</style>
