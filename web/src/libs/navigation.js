/**
 * Post-login redirect handling.
 *
 * The `?next=` parameter is attacker-controllable, so it is only honoured when it
 * resolves to a same-origin *relative* path. Absolute URLs ("https://evil.test"),
 * protocol-relative URLs ("//evil.test"), the backslash variants some browsers still
 * treat as protocol-relative ("/\evil.test") and scheme payloads ("javascript:...")
 * are all discarded in favour of the default landing page, which closes the open
 * redirect on /login.
 */
export const DEFAULT_AUTHENTICATED_PATH = '/home'

// Throwaway origin used purely to resolve the candidate: anything that escapes it is unsafe.
const RESOLUTION_ORIGIN = 'http://pmt.invalid'

export const sanitizeNextPath = (value, fallback = DEFAULT_AUTHENTICATED_PATH) => {
  if (typeof value !== 'string') return fallback

  const candidate = value.trim()

  if (!candidate.startsWith('/')) return fallback

  let resolved

  try {
    resolved = new URL(candidate, RESOLUTION_ORIGIN)
  } catch {
    return fallback
  }

  if (resolved.origin !== RESOLUTION_ORIGIN) return fallback

  const path = `${resolved.pathname}${resolved.search}${resolved.hash}`

  // Never bounce back to the auth pages: that produces a redirect loop after sign-in.
  return path === '/' || path.startsWith('/login') ? fallback : path
}
