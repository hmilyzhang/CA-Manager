import { createApp } from 'vue'
import ElementPlus from 'element-plus'
import 'element-plus/dist/index.css'
import zhCn from 'element-plus/es/locale/lang/zh-cn'
import en from 'element-plus/es/locale/lang/en'
import * as Icons from '@element-plus/icons-vue'
import App from './App.vue'
import router from './router.js'
import { i18n, curLang } from './i18n.js'
import './style.css'

const app = createApp(App)
for (const [name, comp] of Object.entries(Icons)) app.component(name, comp)
app.use(ElementPlus, { locale: curLang() === 'zh' ? zhCn : en })
app.use(i18n)
app.use(router)
app.mount('#app')
