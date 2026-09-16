'use client'

import { createContext, useContext, useEffect, useState } from 'react'

import { usePathname, useRouter } from 'next/navigation'

import { authService } from '@/services/auth'
import { clearSession, loadSession, saveSession } from '@/libs/session'
import { onSessionExpired, onForbidden, onSessionRefreshed } from '@/libs/authEvents'
import { sanitizeNextPath } from '@/libs/navigation'

const AuthContext = createContext(null)

export const AuthProvider = ({ children }) => {
  const router = useRouter()
  const pathname = usePathname()
  const [session, setSession] = useState(null)
  const [ready, setReady] = useState(false)

  useEffect(() => {
    setSession(loadSession())
    setReady(true)
  }, [])

  // Cross-module auth events (fired from the axios refresh interceptor) keep
  // the React session state in sync with the storage layer and the router.
  useEffect(() => {
    const unsubscribeExpired = onSessionExpired(() => {
      clearSession()
      setSession(null)
      router.replace(`/login?next=${encodeURIComponent(sanitizeNextPath(pathname))}`)
    })

    const unsubscribeForbidden = onForbidden(() => {
      router.replace('/forbidden')
    })

    // A silent refresh rotates the tokens in storage only. Without this the context
    // (and every consumer: AbilityProvider, UserDropdown, logout's revoke call) keeps
    // serving the pre-refresh session, so logout revokes an already-rotated token and
    // permission changes never reach the UI.
    const unsubscribeRefreshed = onSessionRefreshed(nextSession => {
      setSession(nextSession ?? loadSession())
    })

    return () => {
      unsubscribeExpired()
      unsubscribeForbidden()
      unsubscribeRefreshed()
    }
  }, [router, pathname])

  const login = async ({ userNameOrEmail, password }) => {
    const data = await authService.login({ userNameOrEmail, password })
    const nextSession = saveSession(data)

    setSession(nextSession)

    return nextSession
  }

  const logout = async () => {
    const refreshToken = session?.refreshToken

    try {
      if (refreshToken) await authService.revoke(refreshToken)
    } catch {
      // A failed revoke must not strand the user in a signed-in shell.
    } finally {
      clearSession()
      setSession(null)
      router.push('/login')
    }
  }

  return <AuthContext.Provider value={{ session, ready, login, logout }}>{children}</AuthContext.Provider>
}

export const useAuth = () => useContext(AuthContext)
