'use client'

import { createContext, Suspense, useContext, useEffect, useMemo, useState } from 'react'

// Hook Imports
import { useAuth } from '@/contexts/AuthContext'
import { createNotificationHub } from '@/libs/signalr'
import ProjectRealtimeSync from './ProjectRealtimeSync'

const SignalRContext = createContext(null)

export const SignalRProvider = ({ children }) => {
  const { session, ready } = useAuth()

  // State rather than a ref so consumers (and the ProjectRealtimeSync child below) re-run their
  // effects when the connection is created or torn down; a ref would hand them a stale null.
  const [connection, setConnection] = useState(null)
  const [connected, setConnected] = useState(false)

  useEffect(() => {
    if (!ready) return

    if (!session) {
      setConnected(false)
      setConnection(null)

      return
    }

    let cancelled = false

    // Named `hub` so it does not shadow the `connection` state above.
    const hub = createNotificationHub()

    if (!hub) {
      return
    }

    hub.on('notification', payload => {
      window.dispatchEvent(new CustomEvent('pmt:notification', { detail: payload }))
    })

    // Keep the exposed `connected` flag in step with the automatic-reconnect lifecycle: without
    // these it would stay `true` after a silent drop and never recover its value on reconnect.
    // Guarded by `cancelled` so a late callback cannot revive state after unmount.
    // ProjectRealtimeSync hangs its group re-join off these same transitions.
    hub.onreconnecting(() => {
      if (!cancelled) setConnected(false)
    })

    hub.onreconnected(() => {
      if (!cancelled) setConnected(true)
    })

    hub.onclose(() => {
      if (!cancelled) setConnected(false)
    })

    // Cleanup must wait for start() to settle before calling stop() — stopping mid-negotiation
    // (e.g. React Strict Mode's double-effect in dev) is what SignalR logs as a console error.
    const startPromise = hub
      .start()
      .then(() => {
        if (cancelled) return

        setConnected(true)
      })
      .catch(() => {
        // Realtime is an enhancement; the dropdown still polls the REST endpoint.
      })

    setConnection(hub)

    return () => {
      cancelled = true
      setConnected(false)
      setConnection(null)

      startPromise.finally(() => hub.stop().catch(() => {}))
    }
  }, [ready, session])

  const value = useMemo(() => ({ connected, connection }), [connected, connection])

  return (
    <SignalRContext.Provider value={value}>
      {/*
        Renders nothing: it joins/leaves the hub's "project:{projectKey}" group as the user moves
        between projects and invalidates the matching react-query caches on "entityChanged".
        Only mounted once a connection exists, so its URL/lookup hooks never run on the signed-out
        shell (the provider also wraps the login/forbidden pages). The Suspense boundary is
        required because it reads useSearchParams.
      */}
      {connection ? (
        <Suspense fallback={null}>
          <ProjectRealtimeSync connection={connection} connected={connected} />
        </Suspense>
      ) : null}
      {children}
    </SignalRContext.Provider>
  )
}

export const useSignalR = () => useContext(SignalRContext)
