<template>
  <el-container style="height: 100%">
    <el-aside width="216px" class="sidebar">
      <div class="logo">
        <el-icon :size="22"><Lock /></el-icon>
        <span>CA-Manager</span>
      </div>
      <el-menu
        :default-active="$route.path"
        router
        background-color="#1d2535"
        text-color="#aeb6c6"
        active-text-color="#ffffff"
        style="border-right: none">
        <el-menu-item index="/dashboard"><el-icon><Odometer /></el-icon>{{ $t('nav.dashboard') }}</el-menu-item>
        <el-menu-item index="/certificates"><el-icon><Postcard /></el-icon>{{ $t('nav.certificates') }}</el-menu-item>
        <el-menu-item index="/expiring"><el-icon><Timer /></el-icon>{{ $t('nav.expiring') }}</el-menu-item>
        <el-menu-item index="/requests" v-if="roleAtLeast(role, 'Operator') || role === 'Auditor'"><el-icon><List /></el-icon>{{ $t('nav.requests') }}</el-menu-item>
        <el-menu-item index="/approvals"><el-icon><Stamp /></el-icon>{{ $t('nav.approvals') }}</el-menu-item>
        <el-menu-item index="/new-request" v-if="role !== 'Auditor'"><el-icon><Upload /></el-icon>{{ $t('nav.newRequest') }}</el-menu-item>
        <el-menu-item index="/pgp" v-if="role !== 'Auditor'"><el-icon><Key /></el-icon>{{ $t('nav.pgp') }}</el-menu-item>
        <el-menu-item index="/templates"><el-icon><Files /></el-icon>{{ $t('nav.templates') }}</el-menu-item>
        <el-menu-item index="/ca"><el-icon><Setting /></el-icon>{{ $t('nav.ca') }}</el-menu-item>
        <el-menu-item index="/notify" v-if="roleAtLeast(role, 'Admin')"><el-icon><Bell /></el-icon>{{ $t('nav.notify') }}</el-menu-item>
        <el-menu-item index="/users" v-if="roleAtLeast(role, 'Admin')"><el-icon><User /></el-icon>{{ $t('nav.users') }}</el-menu-item>
        <el-menu-item index="/audit" v-if="roleAtLeast(role, 'Admin') || role === 'Auditor'"><el-icon><Document /></el-icon>{{ $t('nav.audit') }}</el-menu-item>
      </el-menu>
      <div class="sidebar-foot">CA-Manager v1.5.1</div>
    </el-aside>

    <el-container>
      <el-header class="topbar">
        <div></div>
        <div style="display: flex; align-items: center; gap: 14px">
          <el-dropdown @command="onLang" trigger="click">
            <span class="lang-chip clickable">
              <el-icon><Switch /></el-icon>
              {{ curLang() === 'zh' ? '中文' : 'English' }}
            </span>
            <template #dropdown>
              <el-dropdown-menu>
                <el-dropdown-item command="zh" :disabled="curLang() === 'zh'">简体中文</el-dropdown-item>
                <el-dropdown-item command="en" :disabled="curLang() === 'en'">English</el-dropdown-item>
              </el-dropdown-menu>
            </template>
          </el-dropdown>
          <el-dropdown @command="onCommand">
            <span class="user-chip">
              <el-icon><UserFilled /></el-icon>
              {{ store.user?.username }}
              <el-tag size="small" effect="plain">{{ roleLabel(store.user?.role) }}</el-tag>
            </span>
            <template #dropdown>
              <el-dropdown-menu>
                <el-dropdown-item command="logout">{{ $t('logout') }}</el-dropdown-item>
              </el-dropdown-menu>
            </template>
          </el-dropdown>
        </div>
      </el-header>
      <el-main style="padding: 0; overflow: auto">
        <router-view :key="viewKey" />
      </el-main>
    </el-container>
  </el-container>
</template>

<script setup>
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { store, roleAtLeast, roleLabel } from '../store.js'
import { api } from '../api.js'
import { curLang, setLang } from '../i18n.js'

const router = useRouter()
const route = useRoute()
const { t } = useI18n()
const role = computed(() => store.user?.role || 'Viewer')
const viewKey = computed(() => route.path + '-' + curLang())

function onLang(lang) {
  setLang(lang)
}

async function onCommand(cmd) {
  if (cmd === 'logout') {
    try { await api.post('/api/auth/logout') } catch {}
    store.user = null
    router.push('/login')
  }
}
</script>

<style scoped>
.sidebar {
  background: #1d2535;
  display: flex;
  flex-direction: column;
}
.logo {
  color: #fff;
  font-size: 19px;
  font-weight: 700;
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 20px 22px 16px;
  letter-spacing: 0.5px;
}
.sidebar-foot {
  margin-top: auto;
  padding: 14px 22px;
  font-size: 12px;
  color: #64708a;
}
.topbar {
  background: #fff;
  border-bottom: 1px solid #e4e7ed;
  display: flex;
  align-items: center;
  justify-content: space-between;
  height: 54px;
}

.user-chip {
  display: flex;
  align-items: center;
  gap: 8px;
  cursor: pointer;
  color: #303133;
  outline: none;
}
.lang-chip {
  display: flex;
  align-items: center;
  gap: 5px;
  cursor: pointer;
  color: #606266;
  font-size: 13px;
  outline: none;
}
.el-menu-item.is-active { background: #2563eb !important; }
</style>
