<template>
  <el-config-provider :locale="elLocale">
    <router-view />
  </el-config-provider>
</template>

<script setup>
import { computed } from 'vue'
import zhCn from 'element-plus/es/locale/lang/zh-cn'
import en from 'element-plus/es/locale/lang/en'
import { curLang } from './i18n.js'

const elLocale = computed(() => (curLang() === 'zh' ? zhCn : en))
</script>
