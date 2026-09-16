import { endpoints } from '@/api/endpoints'
import { httpDelete, httpGet, httpPost } from '@/api/httpClient'

/**
 * Client for AiAgentController (`api/v1/ai/agent`).
 *
 * Two API facts shape every call in this module:
 *
 *  - The controller is `[Authorize]` only. There is no `ai.*` permission key (the 17 seeded
 *    keys are fixed by migration), so *any* signed-in user may chat and the agent then acts
 *    strictly inside that user's own permission set, enforced per tool call by the
 *    orchestrator. Only the two reindex endpoints are gated, and on the `Administrator`
 *    *role* rather than on a permission.
 *
 *  - The chat panel is mounted on every dashboard page. A failure here must never navigate
 *    the user away from the page they are working on, so every call sets
 *    `__suppressForbidden` to opt out of the global 403 -> /forbidden redirect installed in
 *    api/interceptors.js (same reasoning as notifications/comments).
 */
const AI_REQUEST = { __suppressForbidden: true }

/** Mirrors ChatRequestValidator.MaxMessageLength; a longer turn is rejected with a 400. */
export const AI_MAX_MESSAGE_LENGTH = 4000

/**
 * Client-side ceiling for one agent turn.
 *
 * The server bounds a turn with `Ai:MaxTurnSeconds` (180s) whichever chat provider is active,
 * and answers an over-budget turn in words rather than failing it. This sits just above that so
 * the server's own explanation wins the race: an axios abort would leave the user with a generic
 * "taking longer than expected" for a turn that did finish, and the ~20s margin covers the
 * embedding, the transcript writes and the response itself. Raising this without raising
 * `Ai:MaxTurnSeconds` buys nothing — and lowering `Ai:MaxTurnSeconds` below this is the only
 * safe direction to change them independently. Every other call uses the axios default.
 */
const CHAT_TIMEOUT_MS = 200000

/** Sessions are short-lived and few; one page covers the sidebar without paging controls. */
const SESSION_PAGE_SIZE = 50

export const aiAgentService = {
  /**
   * Runs one agent turn. `sessionId: null` makes the API create a session and derive its
   * title from this message. Resolves to ChatResponseDto:
   * `{ sessionId, message, toolCalls, citations, steps, truncated, requiresConfirmation,
   * pendingActions, confirmationToken }`.
   *
   * When the model wants to delete something the turn stops short of executing it: the
   * response comes back with `requiresConfirmation: true`, the `pendingActions` it intends to
   * run, and a `confirmationToken` to hand back to `confirm` once the user has decided.
   */
  chat: ({ sessionId = null, message, projectId = null }) =>
    httpPost(
      endpoints.ai.chat,
      { sessionId: sessionId ?? null, message, projectId: projectId ?? null },
      { ...AI_REQUEST, timeout: CHAT_TIMEOUT_MS }
    ),

  /**
   * Resolves a parked destructive turn. `action: 'approve'` executes the pending deletes and
   * answers with the final assistant message; `'cancel'` throws them away and answers with a
   * "nothing was deleted" message. Either way the shape is the same ChatResponseDto as
   * `chat`, so the caller can fold the result into the transcript the same way.
   *
   * Approving replays the tool calls server-side, so this needs the chat timeout too - the
   * axios default would abandon a slow-but-successful delete.
   */
  confirm: ({ token, action }) =>
    httpPost(endpoints.ai.confirm, { token, action }, { ...AI_REQUEST, timeout: CHAT_TIMEOUT_MS }),

  /** PagedResult<ChatSessionDto>: `{ items, page, pageSize, totalCount }`, newest first. */
  listSessions: ({ page = 1, pageSize = SESSION_PAGE_SIZE } = {}) =>
    httpGet(endpoints.ai.sessions, { ...AI_REQUEST, params: { page, pageSize } }),

  /**
   * Creates an empty conversation and returns `{ id }`. The UI prefers lazy creation via
   * `chat` (the API derives a readable title from the first message, and an abandoned
   * "New chat" click leaves no untitled row behind), so this exists for callers that need a
   * session id up front - e.g. pre-scoping a conversation to a project before any message.
   */
  createSession: ({ title = null, projectId = null } = {}) =>
    httpPost(endpoints.ai.sessions, { title, projectId }, AI_REQUEST),

  /** ChatMessageDto[] - a bare array, not paged. Tool calls are not part of the transcript. */
  listMessages: sessionId => httpGet(endpoints.ai.messages(sessionId), AI_REQUEST),

  /** Soft-deletes the conversation. 404 when it is not the caller's own session. */
  deleteSession: sessionId => httpDelete(endpoints.ai.session(sessionId), AI_REQUEST),

  /**
   * Admin only. Answers 202 with an AiReindexStatus immediately (a full rebuild embeds every
   * indexable entity and outlives the request), or 409 when a rebuild is already running.
   */
  reindex: ({ force = false } = {}) =>
    httpPost(endpoints.ai.reindex, null, { ...AI_REQUEST, params: { force } }),

  /** Admin only. `{ isRunning, startedAt, completedAt, lastResult, lastError }`. */
  getReindexStatus: () => httpGet(endpoints.ai.reindexStatus, AI_REQUEST)
}
