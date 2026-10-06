import { i18n } from './i18n.js'

const t = (key, params) => i18n.global.t(key, params ?? {})

async function request(method, url, body) {
  const opts = { method, credentials: 'same-origin', headers: {} }
  if (body !== undefined && body !== null) {
    opts.headers['Content-Type'] = 'application/json'
    opts.body = JSON.stringify(body)
  }
  const resp = await fetch(url, opts)
  if (resp.status === 401) {
    window.dispatchEvent(new CustomEvent('auth:required'))
    throw new Error(t('common.sessionExpired'))
  }
  if (resp.status === 403) {
    const data = await resp.json().catch(() => ({}))
    throw new Error(data.error || t('common.forbidden'))
  }
  if (!resp.ok) {
    const data = await resp.json().catch(() => ({}))
    throw new Error(data.error || `${t('common.requestFailed')} (${resp.status})`)
  }
  const ct = resp.headers.get('content-type') || ''
  if (ct.includes('application/json')) return resp.json()
  return resp
}

export const api = {
  get: (url) => request('GET', url),
  post: (url, body) => request('POST', url, body),
  put: (url, body) => request('PUT', url, body),
  del: (url) => request('DELETE', url),
  download: async (url, filename) => {
    const resp = await request('GET', url)
    const blob = await resp.blob()
    const a = document.createElement('a')
    a.href = URL.createObjectURL(blob)
    a.download = filename
    a.click()
    URL.revokeObjectURL(a.href)
  },
}
