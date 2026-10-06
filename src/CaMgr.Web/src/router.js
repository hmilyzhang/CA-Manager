import { createRouter, createWebHistory } from 'vue-router'
import { store } from './store.js'
import { api } from './api.js'

const routes = [
  { path: '/login', component: () => import('./views/Login.vue'), meta: { public: true } },
  {
    path: '/',
    component: () => import('./layout/MainLayout.vue'),
    children: [
      { path: '', redirect: '/dashboard' },
      { path: 'dashboard', component: () => import('./views/Dashboard.vue') },
      { path: 'certificates', component: () => import('./views/Certificates.vue') },
      { path: 'certificates/:id', component: () => import('./views/CertificateDetail.vue') },
      { path: 'expiring', component: () => import('./views/Expiring.vue') },
      { path: 'requests', component: () => import('./views/Requests.vue') },
      { path: 'approvals', component: () => import('./views/Approvals.vue') },
      { path: 'new-request', component: () => import('./views/NewRequest.vue') },
      { path: 'templates', component: () => import('./views/Templates.vue') },
      { path: 'ca', component: () => import('./views/CaSettings.vue') },
      { path: 'notify', component: () => import('./views/NotifySettings.vue') },
      { path: 'pgp', component: () => import('./views/PgpTools.vue') },
      { path: 'users', component: () => import('./views/Users.vue') },
      { path: 'audit', component: () => import('./views/Audit.vue') },
    ],
  },
]

const router = createRouter({ history: createWebHistory(), routes })
export default router

router.beforeEach(async (to) => {
  if (to.meta.public) return true
  // full page loads lose in-memory state — restore the session from the auth cookie
  if (!store.user) {
    try {
      store.user = await api.get('/api/auth/me')
    } catch {
      return '/login'
    }
  }
  return true
})

window.addEventListener('auth:required', () => {
  store.user = null
  router.push('/login')
})
