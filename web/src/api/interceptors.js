import { clearSession, getAccessToken, getRefreshToken, saveSession } from '@/libs/session'
import { emitSessionExpired, emitForbidden, emitSessionRefreshed } from '@/libs/authEvents'
import { endpoints } from '@/api/endpoints'

/**
 * Wires the shared auth flow onto an axios instance:
 *  - request interceptor stamps the current access token onto every call
 *  - response interceptor catches a single 401, refreshes the token once
 *    (de-duped via refreshPromise so concurrent 401s don't fire N refreshes),
 *    then replays the original request
 *  - a runtime 403 emits a `forbidden` event so AuthContext can route to
 *    /forbidden (callers that want to swallow a 403, e.g. background lookups,
 *    set `config.__suppressForbidden = true`)
 *
 * `authApi` is a plain axios instance (no interceptors) used only for the
 * refresh call itself, so a failed refresh can't recursively trigger another refresh.
 */
export const attachInterceptors = (api, authApi) => {
  let refreshPromise = null

  api.interceptors.request.use(config => {
    const accessToken = getAccessToken()

    if (accessToken) config.headers.Authorization = `Bearer ${accessToken}`

    return config
  })

  api.interceptors.response.use(undefined, async error => {
    const originalRequest = error.config

    // 403 Forbidden → route to the forbidden page (unless the caller opted out).
    if (error.response?.status === 403 && !originalRequest?.__suppressForbidden) {
      emitForbidden()

      return Promise.reject(error)
    }

    // 401 Unauthorized → attempt a single token refresh.
    if (error.response?.status !== 401 || originalRequest?._retried || !getRefreshToken()) {
      return Promise.reject(error)
    }

    originalRequest._retried = true

    refreshPromise ??= authApi
      .post(endpoints.auth.refresh, { refreshToken: getRefreshToken() })
      .then(({ data }) => {
        const nextSession = saveSession(data)

        // The refresh rotates the access AND refresh token and re-issues permissions.
        // Publishing it keeps AuthContext (and therefore AbilityProvider, SignalR and
        // logout's revoke call) from holding the pre-refresh session object.
        emitSessionRefreshed(nextSession)

        return nextSession
      })
      .catch(refreshError => {
        // A failed refresh must clear both the storage layer and the React
        // AuthContext state, otherwise AuthGuard keeps serving protected UI
        // with a stale session object.
        clearSession()
        emitSessionExpired()

        throw refreshError
      })
      .finally(() => {
        refreshPromise = null
      })

    await refreshPromise

    return api(originalRequest)
  })
}
