const SESSION_KEY = 'pmt.session'
const REFRESH_TOKEN_KEY = 'pmt.refresh-token'

let currentSession = null

const canUseStorage = () => typeof window !== 'undefined'

export const loadSession = () => {
  if (currentSession) return currentSession

  if (!canUseStorage()) return null

  try {
    const stored = window.sessionStorage.getItem(SESSION_KEY)

    currentSession = stored ? JSON.parse(stored) : null

    return currentSession
  } catch {
    return null
  }
}

export const saveSession = session => {
  currentSession = session

  if (!canUseStorage()) return session

  window.sessionStorage.setItem(SESSION_KEY, JSON.stringify(session))

  return session
}

export const clearSession = () => {
  currentSession = null

  if (!canUseStorage()) return

  window.sessionStorage.removeItem(SESSION_KEY)
}

export const getAccessToken = () => loadSession()?.accessToken ?? null
export const getRefreshToken = () => loadSession()?.refreshToken ?? null
