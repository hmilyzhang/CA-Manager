import { reactive } from 'vue'
import { i18n } from './i18n.js'

export const store = reactive({
  user: null, // { username, role, source, mustChangePassword }
})

export function roleAtLeast(role, min) {
  const order = { Viewer: 0, Operator: 1, Admin: 2 }
  return (order[role] ?? -1) >= (order[min] ?? 99)
}

export function roleLabel(role) {
  const key = `role.${role}`
  const t = i18n.global.t
  return t(key) === key ? (role || '') : t(key)
}

export async function loadMe(api) {
  try {
    store.user = await api.get('/api/auth/me')
  } catch {
    store.user = null
  }
}
