<template>
  <div class="page">
    <div class="page-title">{{ $t('pgp.title') }}</div>

    <el-alert type="info" :closable="false" style="margin-bottom: 14px" :title="$t('pgp.hint')" />

    <el-row :gutter="14">
      <el-col :span="12">
        <el-card shadow="never">
          <template #header>{{ $t('pgp.form') }}</template>
          <el-form label-width="110px">
            <el-form-item label="Name">
              <el-input v-model="form.name" placeholder="Ops Team" style="width: 280px" />
            </el-form-item>
            <el-form-item label="Email">
              <el-input v-model="form.email" placeholder="ops@corp.local（可选）" style="width: 280px" />
            </el-form-item>
            <el-form-item :label="$t('pgp.algo')">
              <el-select v-model="form.algorithm" style="width: 280px">
                <el-option value="RSA3072" label="RSA 3072" />
                <el-option value="RSA4096" label="RSA 4096" />
                <el-option value="ECC" :label="$t('pgp.algoEcc')" />
              </el-select>
            </el-form-item>
            <el-form-item :label="$t('pgp.passphrase')">
              <el-input v-model="form.password" type="password" show-password style="width: 280px" :placeholder="$t('pgp.pwdLen')" />
            </el-form-item>
            <el-form-item :label="$t('pgp.passphrase2')">
              <el-input v-model="form.password2" type="password" show-password style="width: 280px" />
            </el-form-item>
            <el-form-item :label="$t('pgp.validity')">
              <el-select v-model="form.validityYears" style="width: 280px">
                <el-option :value="0" :label="$t('pgp.never')" />
                <el-option v-for="y in [1,2,3,5,10]" :key="y" :value="y" :label="$t('pgp.years', { n: y })" />
              </el-select>
            </el-form-item>
            <el-form-item>
              <el-button type="primary" :loading="generating" @click="generate">{{ $t('pgp.generate') }}</el-button>
            </el-form-item>
          </el-form>
        </el-card>
      </el-col>

      <el-col :span="12">
        <el-card v-if="result" shadow="never">
          <template #header>{{ $t('pgp.result') }}</template>
          <el-descriptions :column="1" border size="small" style="margin-bottom: 12px">
            <el-descriptions-item label="Key ID"><span class="mono">{{ result.keyId }}</span></el-descriptions-item>
            <el-descriptions-item :label="$t('pgp.fingerprint')"><span class="mono" style="font-size:12px">{{ result.fingerprint }}</span></el-descriptions-item>
            <el-descriptions-item label="User ID">{{ result.userId }}</el-descriptions-item>
            <el-descriptions-item :label="$t('pgp.algo')">{{ result.algorithm }}</el-descriptions-item>
            <el-descriptions-item :label="$t('pgp.validity')">
              {{ fmt(result.createdAt) }} ~ {{ result.expiresAt ? fmt(result.expiresAt) : $t('pgp.never') }}
            </el-descriptions-item>
          </el-descriptions>

          <el-alert type="warning" :closable="false" style="margin-bottom: 12px"
            :title="$t('pgp.warnOnce')" />

          <div style="display:flex; gap:10px; flex-wrap:wrap">
            <el-button type="primary" @click="dl(result.publicKeyAsc, result.fileNameBase + '-pub.asc')">{{ $t('pgp.dlPub') }}</el-button>
            <el-button type="warning" plain @click="dl(result.privateKeyAsc, result.fileNameBase + '-priv.asc')">{{ $t('pgp.dlPriv') }}</el-button>
            <el-button @click="copy(result.publicKeyAsc)">{{ $t('pgp.copyPub') }}</el-button>
          </div>

          <el-divider />
          <div style="font-size:12px; color:#909399; line-height:1.7">
            <div>{{ $t('pgp.usage') }}：</div>
            <pre class="mono" style="background:#f5f7fa;padding:8px;border-radius:6px;font-size:11px;white-space:pre-wrap">gpg --import {{ result.fileNameBase }}-pub.asc
gpg --import {{ result.fileNameBase }}-priv.asc
gpg -e -r "{{ result.userId }}" file.txt
gpg -d file.txt.gpg</pre>
          </div>
        </el-card>
      </el-col>
    </el-row>
  </div>
</template>

<script setup>
import { onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { api } from '../api.js'
import { fmtDate } from '../i18n.js'

const { t } = useI18n()
const form = ref({ name: '', email: '', algorithm: 'RSA3072', password: '', password2: '', validityYears: 2 })
const generating = ref(false)
const result = ref(null)

const fmt = (d) => fmtDate(d)

async function generate() {
  if (!form.value.name.trim()) return ElMessage.warning(t('pgp.needName'))
  if (form.value.password.length < 8) return ElMessage.warning(t('pgp.pwdLen'))
  if (form.value.password !== form.value.password2) return ElMessage.warning(t('pgp.pwdMismatch'))
  generating.value = true
  result.value = null
  try {
    result.value = await api.post('/api/tools/pgp/generate', {
      name: form.value.name.trim(),
      email: form.value.email.trim(),
      algorithm: form.value.algorithm,
      password: form.value.password,
      validityYears: form.value.validityYears,
    })
    ElMessage.success(t('pgp.done'))
  } catch (e) {
    ElMessage.error(e.message)
  } finally {
    generating.value = false
  }
}

function dl(text, filename) {
  const a = document.createElement('a')
  a.href = URL.createObjectURL(new Blob([text], { type: 'application/pgp-keys' }))
  a.download = filename
  a.click()
  URL.revokeObjectURL(a.href)
}

async function copy(text) {
  await navigator.clipboard.writeText(text)
  ElMessage.success(t('pgp.copied'))
}
</script>
