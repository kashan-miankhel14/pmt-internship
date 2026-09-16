'use client'

// React Imports
import { useState } from 'react'

// Third-party Imports
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ToastContainer } from 'react-toastify'

// Hook Imports
import { AbilityProvider } from '@/contexts/AbilityContext'
import { AuthProvider, useAuth } from '@/contexts/AuthContext'
import { SignalRProvider } from '@/contexts/SignalRProvider'

import 'react-toastify/dist/ReactToastify.css'

// Stable identity for "no permissions yet": `session?.permissions ?? []` handed AbilityProvider
// a brand new array on every render, which defeated its useMemo and rebuilt the CASL ability
// (and therefore re-rendered every `Can` / `useAbility` consumer) on each navigation.
const NO_PERMISSIONS = []

const PermissionProvider = ({ children }) => {
  const { session } = useAuth()

  return <AbilityProvider permissions={session?.permissions ?? NO_PERMISSIONS}>{children}</AbilityProvider>
}

// staleTime was 0, so every query was stale the moment it resolved and each remount refired the
// request. Page components mount their queries on every navigation, which meant the same list
// and lookup GETs ran again on each page change. 30s of freshness collapses those duplicates
// while still refetching often enough for a task tracker; gcTime keeps the cache alive long
// enough for back-navigation to hit it instead of the network.
const queryDefaults = {
  queries: {
    retry: 1,
    refetchOnWindowFocus: false,
    staleTime: 30000,
    gcTime: 300000
  }
}

const AppProviders = ({ children }) => {
  const [queryClient] = useState(() => new QueryClient({ defaultOptions: queryDefaults }))

  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <SignalRProvider>
          <PermissionProvider>{children}</PermissionProvider>
        </SignalRProvider>
      </AuthProvider>
      <ToastContainer position='top-right' autoClose={3500} theme='colored' />
    </QueryClientProvider>
  )
}

export default AppProviders
