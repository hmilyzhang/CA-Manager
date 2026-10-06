<template>
  <div class="page">
    <div class="page-title">
      {{ $t('certs.detail.title') }}
      <el-button size="small" style="margin-left: 12px" @click="$router.back()">{{ $t('common.back') }}</el-button>
    </div>

    <el-row :gutter="14" v-if="cert">
      <el-col :span="15">
        <el-card shadow="never">
          <template #header>
            {{ $t('certs.detail.certInfo') }}
            <el-tag :type="statusTag(cert.status)" style="margin-left: 8px">{{ statusText(cert.status) }}</el-tag>
          </template>
          <el-descriptions :column="1" border size="small">
            <el-descriptions-item :label="$t('common.requestId')">{{ cert.requestId }}</el-descriptions-item>
            <el-descriptions-item :label="$t('common.subject')"><span class="mono">{{ cert.subject || cert.commonName || '—' }}</span></el-descriptions-item>
            <el-descriptions-item :label="$t('common.issuer')"><span class="mono">{{ cert.issuer || '—' }}</span></el-descriptions-item>
            <el-descriptions-item :label="$t('common.serialNumber')"><span class="mono">{{ cert.serialNumber }}</span></el-descriptions-item>
            <el-descriptions-item :label="$t('certs.detail.sha1')"><span class="mono">{{ cert.thumbprint }}</span></el-descriptions-item>
            <el-descriptions-item :label="$t('common.template')">{{ cert.template || '—' }}</el-descriptions-item>
            <el-descriptions-item :label="$t('certs.detail.validity')">
              {{ fmt(cert.notBefore) }} ~ {{ fmt(cert.notAfter) }}
              <el-tag v-if="daysLeft !== null" size="small" :type="daysLeft < 0 ? 'danger' : daysLeft < 30 ? 'warning' : 'success'">
                {{ daysLeft < 0 ? t('dashboard.expiredDays', { n: -daysLeft }) : t('dashboard.daysLeft', { n: daysLeft }) }}
              </el-tag>
            </el-descriptions-item>
            <el-descriptions-item :label="$t('certs.detail.sigAlg')">{{ cert.signatureAlgorithm || '—' }}</el-descriptions-item>
            <el-descriptions-item :label="$t('certs.detail.pubKey')">{{ cert.publicKeyAlgorithm }} {{ cert.keySize || '' }} {{ cert.keySize ? t('certs.detail.bits') : '' }}</el-descriptions-item>
            <el-descriptions-item v-if="cert.san?.length" :label="$t('certs.detail.san')">
              <div style="max-height: 140px; overflow: auto">
                <div v-for="(s, i) in cert.san" :key="i" class="mono" style="font-size: 12px">{{ s }}</div>
              </div>
            </el-descriptions-item>
            <el-descriptions-item v-if="cert.keyUsage?.length" :label="$t('certs.detail.keyUsage')">{{ cert.keyUsage.join('、') }}</el-descriptions-item>
            <el-descriptions-item v-if="cert.enhancedKeyUsage?.length" :label="$t('certs.detail.eku')">{{ cert.enhancedKeyUsage.join('；') }}</el-descriptions-item>
            <el-descriptions-item v-if="cert.chain?.length" :label="$t('certs.detail.chain')">
              <div v-for="(c, i) in cert.chain" :key="i" class="mono" style="font-size: 12px">↳ {{ c }}</div>
            </el-descriptions-item>
            <el-descriptions-item v-if="cert.revokedAt" :label="$t('certs.detail.revokeInfo')">
              <span style="color:#f56c6c">{{ fmt(cert.revokedAt) }} · {{ reasonText(cert.revokedReason) }}</span>
            </el-descriptions-item>
          </el-descriptions>
        </el-card>

        <el-card v-if="cert.pem" shadow="never" style="margin-top: 14px">
          <template #header>{{ $t('certs.detail.pem') }}</template>
          <pre class="mono" style="font-size: 11px; margin: 0; max-height: 180px; overflow: auto">{{ cert.pem }}</pre>
        </el-card>
      </el-col>

      <el-col :span="9">
        <el-card shadow="never">
          <template #header>{{ $t('certs.detail.operations') }}</template>
          <div style="display: flex; flex-direction: column; gap: 10px">
            <el-button type="primary" plain :disabled="!cert.hasCertificate" @click="dl('cer')">{{ $t('certs.detail.dlCer') }}</el-button>
            <el-button type="primary" plain :disabled="!cert.hasCertificate" @click="dl('pem')">{{ $t('certs.detail.dlPem') }}</el-button>
            <el-button v-if="cert.status === 'issued'" type="danger" plain @click="$router.push('/certificates')">{{ $t('certs.detail.revokeHint') }}</el-button>
          </div>
        </el-card>

        <el-card shadow="never" style="margin-top: 14px">
          <template #header>{{ $t('certs.detail.reqInfo') }}</template>
          <el-descriptions :column="1" border size="small">
            <el-descriptions-item :label="$t('common.requester')">{{ cert.requester || '—' }}</el-descriptions-item>
            <el-descriptions-item :label="$t('common.submittedAt')">{{ fmt(cert.submittedAt) }}</el-descriptions-item>
            <el-descriptions-item :label="$t('certs.detail.disposition')">{{ statusText(cert.status) }}</el-descriptions-item>
            <el-descriptions-item v-if="cert.dispositionMessage" :label="$t('certs.detail.dispositionMsg')">{{ cert.dispositionMessage }}</el-descriptions-item>
          </el-descriptions>
        </el-card>

        <el-card v-if="cert.csr" shadow="never" style="margin-top: 14px">
          <template #header>{{ $t('certs.detail.csr') }}</template>
          <el-descriptions :column="1" border size="small">
            <el-descriptions-item :label="$t('common.subject')">{{ cert.csr.subject || '—' }}</el-descriptions-item>
            <el-descriptions-item :label="$t('certs.detail.key')">{{ cert.csr.keySize }} {{ t('certs.detail.bits') }}</el-descriptions-item>
            <el-descriptions-item v-if="cert.csr.san?.length" label="SAN">
              <div v-for="(s, i) in cert.csr.san.slice(0, 20)" :key="i" class="mono" style="font-size:12px">{{ s }}</div>
            </el-descriptions-item>
            <el-descriptions-item v-if="cert.csr.enhancedKeyUsage?.length" label="EKU">{{ cert.csr.enhancedKeyUsage.join('；') }}</el-descriptions-item>
          </el-descriptions>
        </el-card>
      </el-col>
    </el-row>
  </div>
</template>

<script setup>
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { ElMessage } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { api } from '../api.js'
import { fmtDate } from '../i18n.js'

const route = useRoute()
const { t } = useI18n()
const cert = ref(null)
const daysLeft = computed(() => {
  if (!cert.value?.notAfter) return null
  return Math.floor((new Date(cert.value.notAfter) - new Date()) / 86400000)
})

function statusTag(s) {
  return { issued: 'success', revoked: 'danger', pending: 'warning', denied: 'info', failed: 'danger' }[s] || 'info'
}
function statusText(s) {
  const key = `status.${s}`
  return t(key) === key ? s : t(key)
}
const reasonText = (r) => r == null ? '' : (t(`reason.${r}`) === `reason.${r}` ? String(r) : t(`reason.${r}`))
const fmt = (d) => fmtDate(d)

function dl(format) {
  api.download(`/api/certificates/${route.params.id}/download?format=${format}`, `cert-${route.params.id}.${format}`)
}

onMounted(async () => {
  try {
    cert.value = await api.get('/api/certificates/' + route.params.id)
  } catch (e) {
    ElMessage.error(e.message)
  }
})
</script>
