/**
 * Minimal browser-safe pub/sub used to sync auth state across module boundaries
 * without creating a circular import between the axios interceptors and
 * AuthContext.
 *
 * Both modules listen here for cross-cutting auth events:
 *  - `expired`  → token refresh failed; AuthContext clears its React state and
 *                  routes to /login (otherwise the interceptor only clears
 *                  sessionStorage and the stale context keeps AuthGuard from
 *                  re-evaluating).
 *  - `forbidden` → a runtime 403 was received; AuthContext routes to /forbidden.
 *  - `refreshed` → the axios interceptor rotated the tokens; AuthContext replaces its
 *                  session object so the access token, refresh token and permissions the
 *                  React tree renders from are the ones actually in storage.
 *
 * The module is intentionally framework-agnostic so it can be unit-tested in
 * isolation (no Next.js / React imports required).
 */
const listeners = { expired: new Set(), forbidden: new Set(), refreshed: new Set() }

const createChannel = key => ({
  subscribe: callback => {
    listeners[key].add(callback)

    return () => listeners[key].delete(callback)
  },
  emit: payload => {
    listeners[key].forEach(callback => {
      try {
        callback(payload)
      } catch {
        // A broken listener must not prevent the others from running.
      }
    })
  }
})

const expiredChannel = createChannel('expired')
const forbiddenChannel = createChannel('forbidden')
const refreshedChannel = createChannel('refreshed')

export const onSessionExpired = expiredChannel.subscribe
export const emitSessionExpired = expiredChannel.emit

export const onForbidden = forbiddenChannel.subscribe
export const emitForbidden = forbiddenChannel.emit

export const onSessionRefreshed = refreshedChannel.subscribe
export const emitSessionRefreshed = refreshedChannel.emit
