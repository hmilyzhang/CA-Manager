import { reactive } from 'vue'
import { i18n } from './i18n.js'

export const store = reactive({
  user: null, // { username, role, source, mustChangePassword }
})

// action hierarchy: Auditor has read-all rights but performs no actions
export function roleAtLeast(role, min) {
  if (min === 'Operator') return role === 'Operator' || role === 'Admin'
  if (min === 'Admin') return role === 'Admin'
  return false
}

// global read access: Admin and Auditor
export function canSeeAll(role) {
  return role === 'Admin' || role === 'Auditor'
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
