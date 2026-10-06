<template>
  <div class="page">
    <div class="page-title">{{ $t('notify.title') }}</div>

    <el-alert type="info" :closable="false" style="margin-bottom: 14px" :title="$t('notify.hint')" />

    <el-row :gutter="14">
      <el-col :span="12">
        <el-card shadow="never">
          <template #header>{{ $t('notify.smtpSection') }}</template>
          <el-form label-width="150px">
            <el-form-item :label="$t('notify.enabled')">
              <el-switch v-model="cfg.smtpEnabled" />
            </el-form-item>
            <el-form-item :label="$t('notify.host')">
              <el-input v-model="cfg.host" placeholder="smtp.corp.local" style="width: 260px" />
            </el-form-item>
            <el-form-item :label="$t('notify.port')">
              <el-input-number v-model="cfg.port" :min="1" :max="65535" />
            </el-form-item>
            <el-form-item :label="$t('notify.mode')">
              <el-select v-model="cfg.mode" style="width: 300px">
                <el-option value="none" :label="$t('notify.modeNone')" />
                <el-option value="starttls" :label="$t('notify.modeStarttls')" />
                <el-option value="ssl" :label="$t('notify.modeSsl')" />
              </el-select>
            </el-form-item>
            <el-form-item :label="$t('notify.username')">
              <el-input v-model="cfg.username" :placeholder="$t('notify.usernamePh')" style="width: 260px" />
            </el-form-item>
            <el-form-item :label="$t('notify.password')">
              <el-input v-model="cfg.password" type="password" show-password style="width: 260px"
                :placeholder="cfg.hasPassword ? $t('notify.passwordSetPh') : $t('notify.passwordPh')" />
            </el-form-item>
            <el-form-item :label="$t('notify.from')">
              <el-input v-model="cfg.from" placeholder="camgr@corp.local" style="width: 260px" />
            </el-form-item>
            <el-form-item :label="$t('notify.fromName')">
              <el-input v-model="cfg.fromName" style="width: 260px" />
            </el-form-item>
          </el-form>
        </el-card>
      </el-col>

      <el-col :span="12">
        <el-card shadow="never">
          <template #header>{{ $t('notify.ruleSection') }}</template>
          <el-form label-width="150px">
            <el-form-item :label="$t('notify.recipients')">
              <el-input v-model="cfg.recipients" type="textarea" :rows="3" :placeholder="$t('notify.recipientsPh')" class="mono" style="font-size:12px" />
            </el-form-item>
            <el-form-item :label="$t('notify.notifyRequester')">
              <el-switch v-model="cfg.notifyRequester" />
            </el-form-item>
            <el-form-item :label="$t('notify.expiringDays')">
              <el-input-number v-model="cfg.expiringDays" :min="1" :max="365" />
            </el-form-item>
            <el-form-item :label="$t('notify.dailyAt')">
              <el-time-select v-model="cfg.dailyAt" start="00:00" step="00:30" end="23:30" style="width: 140px" />
            </el-form-item>
            <el-form-item :label="$t('notify.lastRun')">
              <span style="color:#909399">{{ cfg.lastRun || '—' }}</span>
            </el-form-item>
          </el-form>
        </el-card>

        <el-card shadow="never" style="margin-top: 14px">
          <template #header>{{ $t('common.action') }}</template>
          <div style="display:flex; gap:10px; flex-wrap:wrap">
            <el-button type="primary" :loading="saving" @click="save">{{ $t('notify.save') }}</el-button>
            <el-button :disabled="!cfg.smtpEnabled" :loading="testing" @click="sendTest">{{ $t('notify.sendTest') }}</el-button>
            <el-button type="success" plain :disabled="!cfg.smtpEnabled" :loading="running" @click="runNow">{{ $t('notify.runNow') }}</el-button>
          </div>
          <div v-if="runResultText" class="mono" style="margin-top:12px; font-size:12px; color:#606266; white-space:pre-wrap">{{ runResultText }}</div>
        </el-card>
      </el-col>
    </el-row>
  </div>
</template>

<script setup>
import { onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { api } from '../api.js'

const { t } = useI18n()
const cfg = reactive({
  smtpEnabled: false, host: '', port: 25, mode: 'none', username: '', password: '',
  hasPassword: false, from: '', fromName: 'CA-Manager',
  recipients: '', notifyRequester: false, expiringDays: 30, dailyAt: '08:00', lastRun: '',
})
const saving = ref(false)
const testing = ref(false)
const running = ref(false)
const runResultText = ref('')

async function load() {
  try {
    const d = await api.get('/api/notify/config')
    Object.assign(cfg, d, { recipients: (d.recipients || []).join(', ') })
    cfg.password = ''
  } catch (e) {
    ElMessage.error(e.message)
  }
}

async function save() {
  saving.value = true
  try {
    await api.put('/api/notify/config', {
      smtpEnabled: cfg.smtpEnabled, host: cfg.host, port: cfg.port, mode: cfg.mode,
      username: cfg.username, password: cfg.password, from: cfg.from, fromName: cfg.fromName,
      recipients: cfg.recipients, notifyRequester: cfg.notifyRequester,
      expiringDays: cfg.expiringDays, dailyAt: cfg.dailyAt,
    })
    ElMessage.success(t('notify.saved'))
    load()
  } catch (e) {
    ElMessage.error(e.message)
  } finally {
    saving.value = false
  }
}

async function sendTest() {
  let to = null
  try {
    const { value } = await ElMessageBox.prompt(t('notify.testTo'), t('notify.sendTest'), {
      inputPlaceholder: cfg.recipients?.split(',')[0]?.trim() || 'user@corp.local',
      inputValue: cfg.recipients?.split(',')[0]?.trim() || '',
      inputPattern: /[^@]+@[^@]+/, inputErrorMessage: t('notify.badAddr'),
    })
    to = value
  } catch { return }
  testing.value = true
  try {
    const r = await api.post(`/api/notify/test?to=${encodeURIComponent(to)}`)
    ElMessage.success(r.message)
  } catch (e) {
    ElMessage.error(e.message)
  } finally {
    testing.value = false
  }
}

async function runNow() {
  running.value = true
  runResultText.value = ''
  try {
    const r = await api.post('/api/notify/run-now')
    if (r.skipped) {
      runResultText.value = t('notify.skippedHint') + '\n' + (r.log || []).join('\n')
      ElMessage.info(t('notify.skipped'))
    } else {
      runResultText.value = t('notify.resultLine', {
        digest: r.digestSentTo, req: r.requesterMails, exp: r.expiringCount,
        ca: r.caCertExpiring ? '✓' : '—', crl: r.crlStale ? '✓' : '—', pending: r.pendingCount,
      }) + '\n' + (r.log || []).join('\n')
      ElMessage.success(t('notify.runDone'))
      load()
    }
  } catch (e) {
    ElMessage.error(e.message)
  } finally {
    running.value = false
  }
}

onMounted(load)
</script>
