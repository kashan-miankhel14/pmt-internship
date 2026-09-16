'use client'

import { useCallback, useEffect, useMemo, useRef, useState } from 'react'

import { usePathname } from 'next/navigation'

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import { aiAgentService } from '@/services/aiAgent'
import { aiErrorMessage, AI_CONFIRM_ACTION, AI_ROLE, PENDING_MESSAGE_ID } from '@/libs/aiChat'
import { useAuth } from '@/contexts/AuthContext'

/**
 * All chat state for the floating panel: which conversation is open, its transcript, the
 * in-flight turn, and the per-turn tool calls.
 *
 * Design notes:
 *
 *  - Sessions are server-owned and private to the user; localStorage only remembers *which*
 *    one was last open. The pointer is keyed by user id so signing in as somebody else on a
 *    shared browser cannot resurrect the previous user's conversation id.
 *
 *  - Nothing is fetched until the panel is opened for the first time. The panel is mounted on
 *    every dashboard page, so eager queries would add two requests to every page load for
 *    users who never open it.
 *
 *  - `POST /chat` is a single request/response: the API has no streaming endpoint yet (SSE is
 *    a later phase in the agent plan), so a turn is modelled as one pending user bubble plus
 *    a working indicator, never as partial assistant text.
 *
 *  - Destructive tool calls never run on the first pass. A turn that wants to delete
 *    something comes back with `requiresConfirmation` plus a `confirmationToken`; that is a
 *    normal answer, not an error, and it is held in `pendingConfirmation` until the user
 *    approves or cancels through `POST /ai/agent/confirm`.
 */

const SESSION_KEY_PREFIX = 'pmt.ai.session'

// Single definition of the transcript query so the mounted observer, the post-turn refresh
// and the cache priming below can never drift apart.
const messagesQueryKey = sessionId => ['ai', 'messages', sessionId]

const messagesQueryOptions = sessionId => ({
  queryKey: messagesQueryKey(sessionId),
  queryFn: () => aiAgentService.listMessages(sessionId)
})

// Session scope is inferred from the route so the agent narrows retrieval to the project the
// user is looking at, exactly as the orchestrator expects (`request.ProjectId ?? session.ProjectId`).
const PROJECT_ROUTE = /^\/projects\/(\d+)(?:\/|$)/

const storageKey = userId => `${SESSION_KEY_PREFIX}.${userId ?? 'anonymous'}`

// Stable identity for "no data yet", so an empty transcript does not invalidate the memo on
// every render.
const EMPTY = []

const readStoredSessionId = userId => {
  if (typeof window === 'undefined') return null

  try {
    return window.localStorage.getItem(storageKey(userId))
  } catch {
    // Private-mode / disabled storage must not break the panel.
    return null
  }
}

const writeStoredSessionId = (userId, sessionId) => {
  if (typeof window === 'undefined') return

  try {
    if (sessionId) window.localStorage.setItem(storageKey(userId), sessionId)
    else window.localStorage.removeItem(storageKey(userId))
  } catch {
    // Ignored: remembering the last conversation is a convenience, not a requirement.
  }
}

export const useAiAgent = () => {
  const queryClient = useQueryClient()
  const pathname = usePathname()
  const { session } = useAuth()
  const userId = session?.userId ?? null

  const [open, setOpen] = useState(false)
  const [everOpened, setEverOpened] = useState(false)
  const [sessionId, setSessionId] = useState(null)
  const [pending, setPending] = useState(null)
  const [error, setError] = useState(null)

  // `{ pendingActions, confirmationToken }` for a turn the API parked because it would delete
  // something. Set means "the dialog is open"; the token is the only way to let those tool
  // calls run, so it is never dropped except on an explicit decision or a session switch.
  const [pendingConfirmation, setPendingConfirmation] = useState(null)

  // messageId -> { toolCalls, steps, truncated }. ChatMessageDto carries no tool calls, so
  // this is the only place they exist; a transcript reloaded from the API shows the answer
  // without the intermediate steps.
  const [turns, setTurns] = useState({})

  const sendingRef = useRef(false)
  const confirmingRef = useRef(false)

  // Mirror of `sessionId` for callbacks that resolve *after* an await: `send` and
  // `confirmPending` capture the conversation they started in, so only a ref can tell them
  // which one the user is looking at by the time the reply lands.
  const sessionIdRef = useRef(null)

  useEffect(() => {
    sessionIdRef.current = sessionId
  }, [sessionId])

  const projectId = useMemo(() => {
    const match = PROJECT_ROUTE.exec(pathname ?? '')

    return match ? Number(match[1]) : null
  }, [pathname])

  // Restore the last conversation once the signed-in user is known.
  useEffect(() => {
    setSessionId(readStoredSessionId(userId))
    setPending(null)
    setError(null)
    setPendingConfirmation(null)
    setTurns({})
  }, [userId])

  const selectSession = useCallback(
    nextSessionId => {
      setSessionId(nextSessionId)
      sessionIdRef.current = nextSessionId
      setPending(null)
      setError(null)

      // A token belongs to the conversation that produced it; leaving it live across a
      // session switch would let a stray click delete something the user is no longer
      // looking at.
      setPendingConfirmation(null)
      writeStoredSessionId(userId, nextSessionId)
    },
    [userId]
  )

  const sessionsQuery = useQuery({
    queryKey: ['ai', 'sessions'],
    queryFn: () => aiAgentService.listSessions(),
    enabled: Boolean(userId) && everOpened,
    staleTime: 30000
  })

  const messagesQuery = useQuery({
    ...messagesQueryOptions(sessionId),
    enabled: Boolean(userId) && Boolean(sessionId) && everOpened,

    // A 404 here is a real answer ("that session is gone, or was never yours"), not a blip,
    // so it must not be retried behind the global retry: 1 default.
    retry: false
  })

  // A remembered id can outlive the session it points at (deleted elsewhere, pruned, or
  // belonging to a different user). Drop the pointer and fall back to a fresh conversation.
  useEffect(() => {
    if (messagesQuery.error?.response?.status === 404) {
      writeStoredSessionId(userId, null)
      setSessionId(null)
    }
  }, [messagesQuery.error, userId])

  const sessions = sessionsQuery.data?.items ?? EMPTY
  const messages = messagesQuery.data ?? EMPTY

  /** Transcript as rendered: persisted turns plus the optimistic user bubble, if any. */
  const transcript = useMemo(() => {
    if (!pending) return messages

    return [
      ...messages,
      {
        id: PENDING_MESSAGE_ID,
        sessionId,
        role: AI_ROLE.user,
        content: pending.content,
        createdAt: pending.createdAt,
        citations: []
      }
    ]
  }, [messages, pending, sessionId])

  const sendMutation = useMutation({
    mutationFn: ({ message, targetSessionId }) =>
      aiAgentService.chat({ sessionId: targetSessionId, message, projectId })
  })

  const confirmMutation = useMutation({
    mutationFn: ({ token, action }) => aiAgentService.confirm({ token, action })
  })

  /**
   * Folds a ChatResponseDto into local state - the same work for a chat turn and for a
   * resolved confirmation, because `POST /confirm` answers with the identical shape.
   * Returns the session id the transcript should be refetched for.
   *
   * A turn takes seconds, and the user is free to switch conversations while it runs. Anything
   * that would move the panel - adopting a server-created session id, remembering it, or
   * opening the confirmation dialog - is therefore applied only while the reply still belongs
   * to the conversation on screen; a late answer from the one they left must not yank them
   * back to it, or offer them a delete they can no longer see the context for. The turn record
   * is kept either way, since it is keyed by message id and hurts nobody.
   */
  const absorbResponse = useCallback(
    (response, targetSessionId) => {
      const stillCurrent = sessionIdRef.current === targetSessionId

      if (stillCurrent && response.sessionId && response.sessionId !== targetSessionId) {
        setSessionId(response.sessionId)
        sessionIdRef.current = response.sessionId
        writeStoredSessionId(userId, response.sessionId)
      }

      if (response.message?.id) {
        setTurns(current => ({
          ...current,
          [response.message.id]: {
            toolCalls: response.toolCalls ?? [],
            steps: response.steps,
            truncated: Boolean(response.truncated)
          }
        }))
      }

      // A parked destructive turn is a successful answer that happens to need a decision, so
      // it opens the dialog rather than raising an error. Anything else clears a stale token:
      // approving, cancelling and asking something new all end the pending decision.
      // Dropping the token when the user has moved on is safe - nothing runs without an
      // explicit approve, and an unredeemed token simply expires.
      if (stillCurrent) {
        setPendingConfirmation(
          response.requiresConfirmation && response.confirmationToken
            ? {
                pendingActions: response.pendingActions ?? [],
                confirmationToken: response.confirmationToken
              }
            : null
        )
      }

      return response.sessionId ?? targetSessionId
    },
    [userId]
  )

  // `mutateAsync` is referentially stable, unlike the mutation object react-query rebuilds on
  // every render; depending on the object would rebuild `send` (and `retry`) each time.
  const { mutateAsync: runChat, isPending: isSending } = sendMutation
  const { mutateAsync: runConfirm, isPending: isConfirming } = confirmMutation

  /**
   * Brings the transcript in line with the server after a turn.
   *
   * The two cases are genuinely different. For a conversation that was already on screen an
   * invalidate is right: an observer is mounted, so it refetches and the await actually waits
   * for it. For a conversation the API has just created there is no observer for that key yet,
   * so `invalidateQueries` matches nothing and resolves immediately - the caller then clears
   * the optimistic bubble while the transcript is still empty, and the user watches their own
   * message disappear and a spinner take its place. Priming the cache instead means the query
   * already has data by the time it mounts under the new session id.
   */
  const syncTranscript = useCallback(
    async (resolvedSessionId, previousSessionId) => {
      if (!resolvedSessionId) return

      if (resolvedSessionId !== previousSessionId) {
        try {
          await queryClient.fetchQuery(messagesQueryOptions(resolvedSessionId))

          return
        } catch {
          // Loading the transcript failing is a separate problem from the turn, which
          // succeeded. Fall through and let the mounted query own the retry and the error UI.
        }
      }

      await queryClient.invalidateQueries({ queryKey: messagesQueryKey(resolvedSessionId) })
    },
    [queryClient]
  )

  const send = useCallback(
    async text => {
      const message = text?.trim()

      // The ref guards against a double submit landing between renders; two turns in flight
      // would interleave against the same session transcript.
      if (!message || sendingRef.current) return

      sendingRef.current = true
      setError(null)
      setPending({ content: message, createdAt: new Date().toISOString() })

      const targetSessionId = sessionId

      try {
        const response = await runChat({ message, targetSessionId })
        const resolvedSessionId = absorbResponse(response, targetSessionId)

        // Awaited so the optimistic bubble is only dropped once the transcript that replaces
        // it is in the cache; clearing earlier makes the user's own message flicker out.
        await syncTranscript(resolvedSessionId, targetSessionId)
        queryClient.invalidateQueries({ queryKey: ['ai', 'sessions'] })
      } catch (sendError) {
        setError({ message: aiErrorMessage(sendError), text })

        // The orchestrator persists the user turn *before* calling the model, so a model
        // outage still leaves it in the transcript. Refetch so the panel matches the server
        // instead of silently discarding a message that was in fact saved.
        if (targetSessionId) {
          await queryClient.invalidateQueries({ queryKey: messagesQueryKey(targetSessionId) })
        } else {
          // Same reasoning, one step earlier: the failed turn may still have created the
          // conversation, and the client never learned its id. Refreshing the list is the
          // only way the user gets back to the message they just typed.
          queryClient.invalidateQueries({ queryKey: ['ai', 'sessions'] })
        }
      } finally {
        setPending(null)
        sendingRef.current = false
      }
    },
    [absorbResponse, queryClient, runChat, sessionId, syncTranscript]
  )

  /**
   * Answers the confirmation dialog. `approve` runs the parked deletes and appends the
   * assistant's report of what it did; `cancel` throws them away and appends a note saying
   * nothing was deleted - both are ordinary turns once the API has replied.
   *
   * A failure keeps the dialog open (the panel shows the error inside it) so a network blip
   * does not silently lose the decision the user already made.
   */
  const confirmPending = useCallback(
    async action => {
      const token = pendingConfirmation?.confirmationToken

      if (!token || confirmingRef.current) return

      // Accepts `'approve'` or `{ action: 'approve' }`, and anything the API would not
      // recognise falls back to `cancel`: on an endpoint whose only side effect is a delete,
      // the safe default has to be "do nothing".
      const requested = typeof action === 'string' ? action : action?.action
      const decision = requested === AI_CONFIRM_ACTION.approve ? AI_CONFIRM_ACTION.approve : AI_CONFIRM_ACTION.cancel

      confirmingRef.current = true
      setError(null)

      const targetSessionId = sessionId

      try {
        const response = await runConfirm({ token, action: decision })
        const resolvedSessionId = absorbResponse(response, targetSessionId)

        await syncTranscript(resolvedSessionId, targetSessionId)
        queryClient.invalidateQueries({ queryKey: ['ai', 'sessions'] })
      } catch (confirmError) {
        setError({ message: aiErrorMessage(confirmError) })
      } finally {
        confirmingRef.current = false
      }
    },
    [absorbResponse, pendingConfirmation, queryClient, runConfirm, sessionId, syncTranscript]
  )

  /**
   * Escape hatch for the dialog itself (backdrop, Escape, or a token the server has already
   * rejected). Nothing is executed server-side without an explicit `approve`, so forgetting
   * the token locally is safe - it simply expires.
   */
  const dismissConfirmation = useCallback(() => setPendingConfirmation(null), [])

  const deleteMutation = useMutation({
    mutationFn: id => aiAgentService.deleteSession(id),
    onSuccess: (_result, id) => {
      if (id === sessionId) selectSession(null)

      queryClient.removeQueries({ queryKey: messagesQueryKey(id) })
      queryClient.invalidateQueries({ queryKey: ['ai', 'sessions'] })
    }
  })

  /**
   * "New chat" only clears the local pointer: the API creates the session on the first
   * message and derives a readable title from it, so an abandoned click leaves no untitled
   * row in the sidebar.
   */
  const startNewSession = useCallback(() => selectSession(null), [selectSession])

  const toggle = useCallback(() => {
    // Queries stay disabled until the panel has been opened at least once (see above), so the
    // flag is set here rather than inside the setOpen updater, which must stay pure.
    setEverOpened(true)
    setOpen(current => !current)
  }, [])

  const close = useCallback(() => setOpen(false), [])
  const clearError = useCallback(() => setError(null), [])

  const retry = useCallback(() => {
    const text = error?.text

    if (!text) return

    setError(null)
    send(text)
  }, [error, send])

  return {
    // panel
    open,
    toggle,
    close,

    // True once the launcher has been clicked. The panel is mounted on every dashboard page,
    // so this also gates the drawer's DOM: see AiChatPanel's `keepMounted`.
    everOpened,

    // conversations
    sessionId,
    sessions,
    sessionsQuery,
    selectSession,
    startNewSession,
    deleteSession: deleteMutation.mutate,
    isDeletingSession: deleteMutation.isPending,

    // transcript
    transcript,
    messagesQuery,

    // What the agent did to produce a given assistant message: the executed ToolCallDto[],
    // the step count and whether it ran out of budget. Only lives for the turns this browser
    // session actually ran - ChatMessageDto carries no tool calls, so a transcript reloaded
    // from the API renders the answer without them.
    turnFor: messageId => turns[messageId],

    // composing
    send,
    retry,
    isSending,
    error,
    clearError,

    // destructive turns awaiting an explicit yes/no
    pendingConfirmation,
    confirmPending,
    dismissConfirmation,
    isConfirming,

    // scope
    projectId
  }
}
