import { extractErrors } from '@/libs/errors'

/**
 * Normalisation helpers shared by the AI chat components, kept out of the components so the
 * API-shaped values (string enums, timezone-less timestamps, ProblemDetails bodies) are
 * interpreted in exactly one place.
 */

// Mirrors PMT.Domain.Enums.AiChatRole. The API registers JsonStringEnumConverter, so these
// arrive as strings, not ordinals. "tool" is deliberately absent: intermediate tool turns are
// audited in AiAgentToolCall and never written to the visible transcript.
export const AI_ROLE = { user: 'User', assistant: 'Assistant', system: 'System' }

// Mirrors PMT.Domain.Enums.AiToolStatus.
export const AI_TOOL_STATUS = { pending: 'Pending', running: 'Running', completed: 'Completed', failed: 'Failed' }

/** Sentinel id for the optimistic user bubble; real ids are BIGINT identities from the API. */
export const PENDING_MESSAGE_ID = 'pending'

/** Mirrors the `action` accepted by `POST /ai/agent/confirm`. */
export const AI_CONFIRM_ACTION = { approve: 'approve', cancel: 'cancel' }

/**
 * First id-ish argument of a parked tool call, used to make a generic line concrete
 * ("Delete task #12"). Arguments arrive as an opaque JSON string, so a malformed or
 * unexpected payload simply yields no suffix rather than breaking the dialog.
 */
const pendingActionTarget = argumentsJson => {
  if (!argumentsJson) return ''

  try {
    const parsed = JSON.parse(argumentsJson)

    if (!parsed || typeof parsed !== 'object') return ''

    const entry = Object.entries(parsed).find(
      ([key, value]) => /id$/i.test(key) && (typeof value === 'number' || typeof value === 'string') && value !== ''
    )

    return entry ? `#${entry[1]}` : ''
  } catch {
    return ''
  }
}

/** "delete_task" / "deleteTask" / "tools.delete-task" -> "Delete task". */
const humaniseToolName = toolName =>
  toolName
    .replace(/[_.-]+/g, ' ')
    .replace(/([a-z\d])([A-Z])/g, '$1 $2')
    .toLowerCase()
    .trim()
    .replace(/^./, character => character.toUpperCase())

/**
 * One human-readable line for a PendingActionDto in the confirmation dialog.
 *
 * The backend writes `summary` for exactly this purpose, so it always wins. The fallback only
 * has the raw tool name and its arguments to work with, which is still far better than
 * showing the user a bare `delete_user_story` identifier before they agree to a delete.
 */
export const describePendingAction = action => {
  const summary = typeof action?.summary === 'string' ? action.summary.trim() : ''

  if (summary) return summary

  const toolName = typeof action?.toolName === 'string' ? action.toolName.trim() : ''

  if (!toolName) return 'Run an action Anna could not describe'

  const target = pendingActionTarget(action?.argumentsJson)

  return target ? `${humaniseToolName(toolName)} ${target}` : humaniseToolName(toolName)
}

/**
 * The reindex endpoints are `[Authorize(Roles = "Administrator")]`, not permission-gated, so
 * the button must test the role claim rather than a CASL ability. This is only a UI hint -
 * the API is the actual authority.
 */
export const AI_ADMIN_ROLE = 'Administrator'

export const isAiAdmin = session => Boolean(session?.roles?.includes(AI_ADMIN_ROLE))

/**
 * Dapper materialises DATETIME2 columns with `Kind = Unspecified`, so the API serialises UTC
 * timestamps with no trailing `Z`. `new Date('2026-08-05T09:41:02')` would then be read as
 * local time and every message would be off by the machine's UTC offset, so the missing
 * designator is restored before parsing.
 */
export const parseApiDate = value => {
  if (!value) return null

  const raw = typeof value === 'string' && !/(z|[+-]\d{2}:?\d{2})$/i.test(value) ? `${value}Z` : value
  const date = new Date(raw)

  return Number.isNaN(date.getTime()) ? null : date
}

export const formatChatTime = value => {
  const date = parseApiDate(value)

  return date ? date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : ''
}

const RELATIVE_STEPS = [
  { limit: 60, divisor: 1, unit: 'second' },
  { limit: 3600, divisor: 60, unit: 'minute' },
  { limit: 86400, divisor: 3600, unit: 'hour' },
  { limit: 604800, divisor: 86400, unit: 'day' }
]

/** "just now" / "3 minutes ago" for the session list; falls back to a date past a week. */
export const formatRelativeTime = value => {
  const date = parseApiDate(value)

  if (!date) return ''

  const seconds = Math.round((Date.now() - date.getTime()) / 1000)

  if (seconds < 45) return 'just now'

  const step = RELATIVE_STEPS.find(item => Math.abs(seconds) < item.limit)

  if (!step) return date.toLocaleDateString()

  const formatter = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' })

  return formatter.format(-Math.round(seconds / step.divisor), step.unit)
}

export const formatDuration = milliseconds => {
  if (milliseconds === null || milliseconds === undefined) return ''

  return milliseconds < 1000 ? `${milliseconds} ms` : `${(milliseconds / 1000).toFixed(1)} s`
}

/** TimeSpan serialises as "hh:mm:ss.fffffff"; show only what is useful in a status chip. */
export const formatTimeSpan = value => {
  if (typeof value !== 'string') return ''

  const [hours, minutes, seconds] = value.split(':')

  if (seconds === undefined) return value

  const wholeSeconds = Math.round(Number.parseFloat(seconds))

  if (Number(hours) > 0) return `${Number(hours)}h ${Number(minutes)}m`

  return Number(minutes) > 0 ? `${Number(minutes)}m ${wholeSeconds}s` : `${wholeSeconds}s`
}

/**
 * Tool payloads are stored as opaque JSON strings (ArgumentsJson / ResultJson). Pretty-print
 * them when they parse and fall back to the raw string when they do not, so a malformed
 * payload is still inspectable instead of rendering as "[object Object]".
 */
export const formatJson = (value, maxLength = 2000) => {
  if (!value) return ''

  let text = value

  try {
    text = JSON.stringify(JSON.parse(value), null, 2)
  } catch {
    text = String(value)
  }

  return text.length > maxLength ? `${text.slice(0, maxLength)}\n... (truncated)` : text
}

/**
 * Status-specific copy for the agent endpoints. The generic errorMessage() helper cannot
 * carry these, because 429 and 503 mean something particular here: the agent has its own
 * per-user hourly budget ("ai-agent" rate-limit policy) and depends on a model backend that
 * is routinely offline or unconfigured in development.
 *
 * 401 is deliberately *not* about the model. The agent endpoints are `[Authorize]`, so a 401
 * is the caller's own bearer token being missing or expired (the interceptor has already
 * tried to refresh it by the time this runs). A provider key problem never reaches the
 * browser as a 401: the API maps AiServiceUnavailableException to 503 with a message naming
 * the setting, which is why 503 prefers the server's own text below.
 */
const AI_STATUS_MESSAGES = {
  401: 'Your session has expired. Please sign in again.',
  403: 'You don\'t have permission to use Anna.',
  404: 'That conversation no longer exists.',
  429: 'Anna is getting too many requests. Please wait a moment and try again.',
  503: 'Anna is temporarily unavailable. Please try again in a moment.'
}

/**
 * Statuses where the API's message beats ours when it sends one.
 *
 * 503 carries the orchestrator's diagnosis verbatim ("No Gemini API key is configured...",
 * "The configured Gemini model was not found..."), and a 429 may be either the framework
 * rate limiter (empty body - our copy is used) or the provider's own quota message relayed
 * through the AI client, which tells the user how long to wait. Both were previously thrown
 * away by the lookup above.
 */
const AI_SERVER_MESSAGE_STATUSES = new Set([429, 503])

/** ProblemDetails text if the body actually carries any; null when there is nothing to show. */
const serverProvidedMessage = data => {
  if (typeof data === 'string') return data.trim() || null

  if (!data || typeof data !== 'object') return null

  const fromErrors = Array.isArray(data.errors)
    ? data.errors.find(entry => typeof entry === 'string' && entry.trim())
    : null

  const message = fromErrors || data.detail || data.title

  return typeof message === 'string' && message.trim() ? message.trim() : null
}

export const aiErrorMessage = error => {
  if (!error) return 'Something went wrong. Please try again.'

  // axios reports its own client-side timeout this way; the server may still be working, so
  // the copy must not claim the request failed outright.
  if (error.code === 'ECONNABORTED' || error.code === 'ETIMEDOUT') {
    return 'Anna is taking longer than expected. She\'s still working on it — try a simpler request or wait a moment.'
  }

  if (error.code === 'ERR_NETWORK') return 'Could not reach the server. Check your connection and try again.'

  const status = error.response?.status

  if (AI_SERVER_MESSAGE_STATUSES.has(status)) {
    return serverProvidedMessage(error.response?.data) ?? AI_STATUS_MESSAGES[status]
  }

  if (status && AI_STATUS_MESSAGES[status]) return AI_STATUS_MESSAGES[status]

  // Everything else (notably 400 from ChatRequestValidator) carries a usable ProblemDetails.
  return extractErrors(error.response?.data ?? { title: error.message })[0]
}
