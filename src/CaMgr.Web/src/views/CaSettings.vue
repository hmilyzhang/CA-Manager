<template>
  <div class="page">
    <div class="page-title">{{ $t('ca.title') }}</div>

    <el-row :gutter="14">
      <el-col :span="12">
        <el-card shadow="never">
          <template #header>{{ $t('ca.authority') }}</template>
          <el-descriptions :column="1" border size="small">
            <el-descriptions-item :label="$t('ca.caName')">{{ ca.name }}</el-descriptions-item>
            <el-descriptions-item :label="$t('ca.caType')"><el-tag size="small">{{ ca.typeCode !== undefined ? $t('caType.' + ca.typeCode) : '—' }}</el-tag></el-descriptions-item>
            <el-descriptions-item :label="$t('ca.config')"><span class="mono">{{ ca.config }}</span></el-descriptions-item>
            <el-descriptions-item :label="$t('ca.dns')">{{ ca.dnsName }}</el-descriptions-item>
            <el-descriptions-item :label="$t('ca.policyModule')">{{ ca.policyModule }}</el-descriptions-item>
            <el-descriptions-item :label="$t('ca.version')">{{ ca.version }}</el-descriptions-item>
            <el-descriptions-item v-if="ca.caCertificate" :label="$t('ca.caCert')">
              <div class="mono" style="font-size: 12px">{{ ca.caCertificate.subject }}</div>
              <div style="font-size: 12px; color: #909399">
                {{ fmt(ca.caCertificate.notBefore) }} ~ {{ fmt(ca.caCertificate.notAfter) }}
              </div>
            </el-descriptions-item>
            <el-descriptions-item label="CDP">
              <div v-for="(u, i) in ca.cdp || []" :key="i" class="mono" style="font-size: 11px; word-break: break-all">{{ u }}</div>
            </el-descriptions-item>
            <el-descriptions-item label="AIA">
              <div v-for="(u, i) in ca.aia || []" :key="i" class="mono" style="font-size: 11px; word-break: break-all">{{ u }}</div>
            </el-descriptions-item>
          </el-descriptions>
        </el-card>
      </el-col>

      <el-col :span="12">
        <el-card shadow="never">
          <template #header>{{ $t('ca.crlPublish') }}</template>
          <el-descriptions :column="2" border size="small" style="margin-bottom: 12px">
            <el-descriptions-item :label="$t('ca.basePeriod')">{{ crl.periodUnits }} {{ periodLabel(crl.periodUnits, crl.period || 'Weeks') }}</el-descriptions-item>
            <el-descriptions-item :label="$t('ca.deltaPeriod')">
              {{ Number(crl.deltaUnits) > 0 ? `${crl.deltaUnits} ${periodLabel(crl.deltaUnits, crl.deltaPeriod || 'Days')}` : $t('ca.disabled') }}
            </el-descriptions-item>
          </el-descriptions>

          <el-form v-if="isAdmin" label-width="120px" style="margin-bottom: 8px">
            <el-form-item :label="$t('ca.baseLabel')">
              <el-input-number v-model="crlForm.baseUnits" :min="1" :max="52" />
              <el-select v-model="crlForm.basePeriod" style="width: 110px; margin-left: 6px">
                <el-option v-for="p in periodKeys" :key="p" :label="$t('period.' + p)" :value="p" />
              </el-select>
            </el-form-item>
            <el-form-item :label="$t('ca.deltaLabel')">
              <el-input-number v-model="crlForm.deltaUnits" :min="0" :max="52" />
              <el-select v-model="crlForm.deltaPeriod" style="width: 110px; margin-left: 6px">
                <el-option v-for="p in periodKeys" :key="p" :label="$t('period.' + p)" :value="p" />
              </el-select>
              <span style="color:#909399;font-size:12px;margin-left:8px">{{ $t('ca.deltaHint') }}</span>
            </el-form-item>
            <el-form-item>
              <el-button type="primary" :loading="saving" @click="savePeriod">{{ $t('ca.savePeriod') }}</el-button>
            </el-form-item>
          </el-form>

          <el-divider />
          <div style="display: flex; gap: 10px; flex-wrap: wrap">
            <el-button type="primary" plain @click="publish(true, false)" :loading="publishing">{{ $t('ca.publishBase') }}</el-button>
            <el-button type="primary" plain @click="publish(true, true)" :loading="publishing">{{ $t('ca.publishBoth') }}</el-button>
            <el-button plain @click="downloadCrl('base')">{{ $t('ca.dlBase') }}</el-button>
            <el-button plain @click="downloadCrl('delta')">{{ $t('ca.dlDelta') }}</el-button>
          </div>
        </el-card>

        <el-card shadow="never" style="margin-top: 14px">
          <template #header>{{ $t('ca.crlContent') }}</template>
          <el-descriptions :column="1" border size="small">
            <el-descriptions-item :label="$t('common.issuer')">{{ crlInfo?.issuer || '—' }}</el-descriptions-item>
            <el-descriptions-item :label="$t('ca.thisUpdate')">{{ fmt(crlInfo?.thisUpdate) }}</el-descriptions-item>
            <el-descriptions-item :label="$t('ca.nextUpdate')">
              <span :style="crlStale ? 'color:#f56c6c;font-weight:600' : ''">{{ fmt(crlInfo?.nextUpdate) }}</span>
            </el-descriptions-item>
            <el-descriptions-item :label="$t('ca.revokedCount')">{{ crlInfo?.revokedCount ?? '—' }}</el-descriptions-item>
          </el-descriptions>
          <div v-if="crlEntries.length" style="margin-top: 10px; max-height: 200px; overflow: auto">
            <el-table :data="crlEntries" size="small">
              <el-table-column :label="$t('common.serialNumber')" min-width="180">
                <template #default="{ row }"><span class="mono" style="font-size:12px">{{ row.serial }}</span></template>
              </el-table-column>
              <el-table-column :label="$t('ca.revokedOn')" width="160">
                <template #default="{ row }">{{ fmt(row.revokedOn) }}</template>
              </el-table-column>
              <el-table-column :label="$t('certs.reasonLabel')" width="140">
                <template #default="{ row }">{{ row.reasonText }}</template>
              </el-table-column>
            </el-table>
          </div>
        </el-card>
      </el-col>
    </el-row>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { api } from '../api.js'
import { store, roleAtLeast } from '../store.js'
import { fmtDate, periodLabel } from '../i18n.js'

const { t } = useI18n()
const isAdmin = computed(() => roleAtLeast(store.user?.role, 'Admin'))
const ca = ref({})
const crl = ref({})
const crlInfo = ref(null)
const crlEntries = ref([])
const saving = ref(false)
const publishing = ref(false)
const periodKeys = ['Hours', 'Days', 'Weeks', 'Months', 'Years']
const crlForm = reactive({ baseUnits: 1, basePeriod: 'Weeks', deltaUnits: 1, deltaPeriod: 'Days' })

const fmt = (d) => fmtDate(d)
const crlStale = computed(() => crlInfo.value?.nextUpdate && new Date(crlInfo.value.nextUpdate) < new Date())

async function reload() {
  try {
    ca.value = await api.get('/api/ca/info')
    crl.value = ca.value.crl || {}
    crlForm.baseUnits = Number(crl.value.periodUnits) || 1
    crlForm.basePeriod = crl.value.period || 'Weeks'
    crlForm.deltaUnits = Number(crl.value.deltaUnits) || 0
    crlForm.deltaPeriod = crl.value.deltaPeriod || 'Days'
    const info = await api.get('/api/ca/crl/detail?kind=base')
    crlInfo.value = info
    crlEntries.value = info?.entries || []
  } catch (e) {
    ElMessage.error(e.message)
  }
}

async function savePeriod() {
  try {
    await ElMessageBox.confirm(t('ca.periodSaveConfirm'), t('ca.periodTitle'), { type: 'warning' })
  } catch { return }
  saving.value = true
  try {
    const r = await api.post('/api/ca/crl/period', {
      baseUnits: crlForm.baseUnits, basePeriod: crlForm.basePeriod,
      deltaUnits: crlForm.deltaUnits, deltaPeriod: crlForm.deltaPeriod,
    })
    ElMessage.success(r.message)
    reload()
  } catch (e) {
    ElMessage.error(e.message)
  } finally {
    saving.value = false
  }
}

async function publish(base, delta) {
  publishing.value = true
  try {
    await api.post('/api/ca/crl/publish', { base, delta })
    ElMessage.success(t('ca.published'))
    setTimeout(reload, 1500)
  } catch (e) {
    ElMessage.error(e.message)
  } finally {
    publishing.value = false
  }
}

function downloadCrl(kind) {
  api.download(`/api/ca/crl?kind=${kind}`, `${kind}-crl.crl`)
}

onMounted(reload)
</script>
