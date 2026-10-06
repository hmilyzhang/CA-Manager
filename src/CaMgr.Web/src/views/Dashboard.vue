<template>
  <div class="page">
    <div class="page-title">{{ $t('dashboard.title') }}</div>

    <el-alert v-if="!data.alive" type="error" :closable="false" show-icon style="margin-bottom: 14px"
      :title="$t('dashboard.caUnreachable', { err: data.pingError || '—' })" />

    <el-row :gutter="14">
      <el-col :span="6" v-for="card in statCards" :key="card.label">
        <el-card class="stat-card" shadow="never">
          <div style="color:#909399;font-size:13px">{{ card.label }}</div>
          <div class="num" :style="{ color: card.color }">{{ card.value }}</div>
        </el-card>
      </el-col>
    </el-row>

    <el-row :gutter="14" style="margin-top: 14px">
      <el-col :span="14">
        <el-card shadow="never">
          <template #header>{{ $t('dashboard.trend') }}</template>
          <div ref="chartEl" style="height: 260px"></div>
        </el-card>
      </el-col>
      <el-col :span="10">
        <el-card shadow="never">
          <template #header>{{ $t('dashboard.caStatus') }}</template>
          <el-descriptions :column="1" border size="small">
            <el-descriptions-item :label="$t('dashboard.caName')">{{ ca.name || '—' }} <el-tag v-if="ca.typeCode !== undefined" size="small">{{ $t('caType.' + ca.typeCode) }}</el-tag></el-descriptions-item>
            <el-descriptions-item :label="$t('dashboard.caConfig')"><span class="mono">{{ ca.config }}</span></el-descriptions-item>
            <el-descriptions-item :label="$t('dashboard.caCertExpiry')">
              <span :style="caCertSoon ? 'color:#e6a23c;font-weight:600' : ''">{{ data.caCertificateExpiry || '—' }}</span>
            </el-descriptions-item>
            <el-descriptions-item :label="$t('dashboard.crlPeriod')">
              {{ ca.crl?.periodUnits }} {{ periodLabel(ca.crl?.periodUnits, ca.crl?.period || 'Weeks') }}
              <template v-if="Number(ca.crl?.deltaUnits) > 0"> · Delta {{ ca.crl?.deltaUnits }} {{ periodLabel(ca.crl?.deltaUnits, ca.crl?.deltaPeriod || 'Days') }}</template>
              <template v-else> · {{ $t('dashboard.disabled') }}</template>
            </el-descriptions-item>
          </el-descriptions>
        </el-card>
        <el-card shadow="never" style="margin-top: 14px">
          <template #header>{{ $t('dashboard.latestOps') }}</template>
          <el-table :data="data.recentAudit || []" size="small" :show-header="false">
            <el-table-column prop="username" width="90" />
            <el-table-column width="110">
              <template #default="{ row }">
                <el-tag :type="row.success ? 'info' : 'danger'" size="small">{{ $t('actionNames.' + row.action, row.action) }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column prop="objectId" />
          </el-table>
        </el-card>
      </el-col>
    </el-row>
  </div>
</template>

<script setup>
import { computed, onMounted, ref, nextTick, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import * as echarts from 'echarts'
import { api } from '../api.js'
import { curLang, periodLabel } from '../i18n.js'

const { t } = useI18n()
const data = ref({ alive: false, totals: {}, trend: [], recentAudit: [] })
const ca = ref({})
const chartEl = ref(null)
let chart = null

const statCards = computed(() => [
  { label: t('dashboard.issued'), value: data.value.totals?.issued ?? '—', color: '#2563eb' },
  { label: t('dashboard.pending'), value: data.value.totals?.pending ?? '—', color: '#e6a23c' },
  { label: t('dashboard.revoked'), value: data.value.totals?.revoked ?? '—', color: '#f56c6c' },
  { label: t('dashboard.expiring'), value: data.value.totals?.expiring30 ?? '—', color: '#9254de' },
])

const caCertSoon = computed(() => {
  if (!data.value.caCertificateExpiry) return false
  return new Date(data.value.caCertificateExpiry).getFullYear() - new Date().getFullYear() < 2
})

function renderChart() {
  if (!chartEl.value) return
  if (!chart) chart = echarts.init(chartEl.value)
  const trend = [...(data.value.trend || [])].reverse()
  chart.setOption({
    grid: { left: 40, right: 16, top: 16, bottom: 24 },
    tooltip: { trigger: 'axis' },
    xAxis: { type: 'category', data: trend.map(x => x.date.slice(5)) },
    yAxis: { type: 'value', minInterval: 1 },
    series: [{ type: 'bar', data: trend.map(x => x.count), barMaxWidth: 22, itemStyle: { color: '#2563eb', borderRadius: [4, 4, 0, 0] } }],
  })
}

watch(curLang, () => renderChart())
window.addEventListener('resize', () => chart?.resize())

onMounted(async () => {
  const [d, c] = await Promise.all([
    api.get('/api/dashboard'),
    api.get('/api/ca/info').catch(() => ({})),
  ])
  data.value = d
  ca.value = c
  await nextTick()
  renderChart()
})
</script>
