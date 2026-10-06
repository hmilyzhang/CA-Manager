<template>
  <div class="login-wrap">
    <el-card class="login-card">
      <div class="login-head">
        <el-icon :size="30" color="#2563eb"><Lock /></el-icon>
        <h2>{{ $t('login.title') }}</h2>
        <p class="sub">{{ $t('login.subtitle') }}</p>
      </div>
      <el-form @submit.prevent="doLogin">
        <el-form-item>
          <el-input v-model="username" :placeholder="$t('login.username')" size="large" autofocus>
            <template #prefix><el-icon><User /></el-icon></template>
          </el-input>
        </el-form-item>
        <el-form-item>
          <el-input v-model="password" type="password" :placeholder="$t('login.password')" size="large" show-password @keyup.enter="doLogin">
            <template #prefix><el-icon><Key /></el-icon></template>
          </el-input>
        </el-form-item>
        <el-button type="primary" size="large" style="width: 100%" :loading="loading" @click="doLogin">{{ $t('login.button') }}</el-button>
      </el-form>
      <div class="lang-row">
        <el-radio-group :model-value="curLang()" size="small" @change="onLang">
          <el-radio-button value="zh">中文</el-radio-button>
          <el-radio-button value="en">English</el-radio-button>
        </el-radio-group>
      </div>
      <p class="hint">{{ $t('login.hint') }}</p>
    </el-card>

    <el-dialog v-model="showChange" :title="$t('login.changeTitle')" width="420" :close-on-click-modal="false" :show-close="false">
      <el-form label-width="80px">
        <el-form-item :label="$t('login.newPassword')">
          <el-input v-model="newPassword" type="password" show-password :placeholder="$t('login.pwdLen')" />
        </el-form-item>
        <el-form-item :label="$t('login.confirmPwd')">
          <el-input v-model="newPassword2" type="password" show-password />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button type="primary" @click="doChangePassword">{{ $t('login.save') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { api } from '../api.js'
import { store } from '../store.js'
import { curLang, setLang } from '../i18n.js'

const router = useRouter()
const { t } = useI18n()
const username = ref('')
const password = ref('')
const loading = ref(false)
const showChange = ref(false)
const newPassword = ref('')
const newPassword2 = ref('')

function onLang(v) { setLang(v) }

async function doLogin() {
  if (!username.value || !password.value) return
  loading.value = true
  try {
    const me = await api.post('/api/auth/login', { username: username.value, password: password.value })
    store.user = me
    if (me.mustChangePassword) {
      showChange.value = true
    } else {
      router.push('/dashboard')
    }
  } catch (e) {
    ElMessage.error(e.message)
  } finally {
    loading.value = false
  }
}

async function doChangePassword() {
  if (newPassword.value.length < 8) return ElMessage.warning(t('login.pwdLen'))
  if (newPassword.value !== newPassword2.value) return ElMessage.warning(t('login.pwdMismatch'))
  try {
    await api.post('/api/auth/change-password', { oldPassword: password.value, newPassword: newPassword.value })
    store.user.mustChangePassword = false
    showChange.value = false
    ElMessage.success(t('login.changed'))
    router.push('/dashboard')
  } catch (e) {
    ElMessage.error(e.message)
  }
}
</script>

<style scoped>
.login-wrap {
  height: 100%;
  display: flex;
  align-items: center;
  justify-content: center;
  background: linear-gradient(135deg, #1d2535 0%, #27324a 55%, #1b3a6b 100%);
}
.login-card { width: 380px; padding: 14px 18px; border-radius: 14px; }
.login-head { text-align: center; margin-bottom: 18px; }
.login-head h2 { margin: 10px 0 4px; font-size: 20px; }
.sub { color: #909399; font-size: 13px; margin: 0; }
.lang-row { text-align: center; margin-top: 12px; }
.hint { color: #a8abb2; font-size: 12px; text-align: center; margin: 14px 0 2px; }
</style>
