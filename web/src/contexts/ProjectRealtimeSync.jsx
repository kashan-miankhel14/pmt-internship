'use client'

import { useEffect, useRef } from 'react'

import { useQueryClient } from '@tanstack/react-query'

import { useCurrentProjectKey } from '@/hooks/useCurrentProjectKey'

/**
 * Server event contract (NotificationsHub):
 *   "entityChanged" -> { entityType, entityId, projectKey, action }
 *   entityType: "Task" | "Issue" | "UserStory" | "Sprint"
 *   action:     "created" | "updated" | "deleted"
 *
 * Hub methods: JoinProject(projectKey) / LeaveProject(projectKey), group "project:{projectKey}".
 */
const ENTITY_CHANGED_EVENT = 'entityChanged'

// entityType -> the module list key that owns it. A Map (not an object literal) because the
// lookup key comes off the wire: an object would resolve "constructor" to Object.prototype's.
// Matched case-insensitively so a serializer casing change on the server degrades to "still
// works" rather than "silently stops refreshing".
const MODULE_KEY_BY_ENTITY = new Map([
  ['task', 'tasks'],
  ['issue', 'issues'],
  ['userstory', 'stories'],
  ['sprint', 'sprints']
])

// Mirrors useCrudModule's DASHBOARD_SYNC_KEYS: only record-count modules move the metric cards.
const DASHBOARD_SYNC_KEYS = new Set(['tasks', 'issues', 'stories'])

/**
 * Invalidates exactly what a local mutation would have invalidated, so a remote change lands the
 * same way an in-tab one does. Keys are quoted from the hooks that own them:
 *   useCrudModule  -> ['tasks' | 'issues' | 'stories', params]  (invalidated by prefix)
 *   useSprints     -> ['sprints', projectKey]
 *   useProjectDetail -> ['project-detail', id]
 *   useBoardColumns  -> ['board-columns', projectKey]
 *   useDashboard     -> ['dashboard']
 */
const invalidateForEntity = (queryClient, moduleKey, projectKey) => {
  if (moduleKey === 'sprints') {
    // useSprints keys its list by projectKey, so the invalidation can be exact.
    queryClient.invalidateQueries({ queryKey: ['sprints', projectKey] })
  } else {
    // useCrudModule's list key is [moduleKey, debouncedParams]; the prefix covers every
    // paging/search/pageSize variant currently cached, exactly like its own invalidate().
    queryClient.invalidateQueries({ queryKey: [moduleKey] })
  }

  // Project-scoped aggregates. useCrudModule and useSprints both refresh project-detail after a
  // write (its tabs embed stories/tasks/issues/sprints), and board-columns is keyed by the same
  // projectKey the payload carries, so it is invalidated narrowly rather than by prefix.
  queryClient.invalidateQueries({ queryKey: ['project-detail'] })
  queryClient.invalidateQueries({ queryKey: ['board-columns', projectKey] })

  if (DASHBOARD_SYNC_KEYS.has(moduleKey)) {
    queryClient.invalidateQueries({ queryKey: ['dashboard'] })
  }
}

/**
 * Keeps the hub connection subscribed to the project group for whatever project the URL says the
 * user is looking at, and turns the server's "entityChanged" broadcasts into targeted react-query
 * invalidations. Renders nothing.
 *
 * Mounted by SignalRProvider only once a connection exists (i.e. only for a signed-in shell), so
 * the URL/lookup hooks below never run on the login screen.
 */
const ProjectRealtimeSync = ({ connection, connected }) => {
  const queryClient = useQueryClient()
  const projectKey = useCurrentProjectKey()

  // Trailing debounce per module+project: a burst of broadcasts (e.g. a drag fires the PUT and
  // the server echoes entityChanged back, plus teammates) coalesces into ONE refetch instead of
  // N overlapping requests. Overlapping refetches get aborted by the browser, which is what
  // produces the TaskCanceledException noise in the API debugger.
  const debounceTimersRef = useRef(new Map())

  useEffect(() => {
    const timers = debounceTimersRef.current

    return () => {
      for (const timer of timers.values()) {
        clearTimeout(timer)
      }

      timers.clear()
    }
  }, [])

  const scheduleInvalidation = (moduleKey, projectKey) => {
    const timers = debounceTimersRef.current
    const key = `${moduleKey}:${projectKey}`
    const existing = timers.get(key)

    if (existing) clearTimeout(existing)

    timers.set(
      key,
      setTimeout(() => {
        timers.delete(key)
        invalidateForEntity(queryClient, moduleKey, projectKey)
      }, 300)
    )
  }

  // Which group this connection is currently a member of. Not state: it must not trigger renders,
  // and it has to survive the effect re-running for an unrelated dependency. The connection is
  // part of the record because membership is per-connection server state — a silent token refresh
  // swaps the hub instance, and the replacement has joined nothing.
  const joinedRef = useRef({ connection: null, key: null })

  // Group membership does not survive a drop. These handlers only forget the local bookkeeping;
  // the join effect below sees `connected` flip back to true after onreconnected and re-issues
  // JoinProject for whatever project is in context then.
  // Registered once per connection because SignalR has no way to unregister lifecycle callbacks.
  useEffect(() => {
    if (!connection) return

    const forgetGroup = () => {
      joinedRef.current = { connection: null, key: null }
    }

    connection.onreconnecting?.(forgetGroup)
    connection.onreconnected?.(forgetGroup)
    connection.onclose?.(forgetGroup)
  }, [connection])

  // Join the current project's group / leave the previous one.
  useEffect(() => {
    if (!connection || !connected) {
      joinedRef.current = { connection: null, key: null }

      return
    }

    const joined = joinedRef.current

    // Only a group joined on *this* connection can be left on it.
    const previousKey = joined.connection === connection ? joined.key : null

    if (joined.connection === connection && joined.key === projectKey) return

    let cancelled = false

    // Claim the target before awaiting: a re-render while the invoke is in flight must not fire
    // a second JoinProject for the same key.
    joinedRef.current = { connection, key: projectKey }

    const sync = async () => {
      try {
        // Leaving first keeps the connection in at most one project group at a time; when
        // `projectKey` is null (a route with no project in context) that is all that happens.
        if (previousKey) await connection.invoke('LeaveProject', previousKey)
        if (!cancelled && projectKey) await connection.invoke('JoinProject', projectKey)
      } catch {
        // Realtime is an enhancement — the lists still refetch on their own staleTime. Drop the
        // bookkeeping so the next dependency change retries the join.
        if (!cancelled) joinedRef.current = { connection: null, key: null }
      }
    }

    sync()

    return () => {
      cancelled = true
    }
  }, [connection, connected, projectKey])

  // Subscribe to the broadcast. Re-subscribing when `projectKey` changes is safe: the cleanup
  // detaches the previous handler, so a re-render can never leave two of them attached.
  useEffect(() => {
    if (!connection) return

    const handleEntityChanged = payload => {
      if (!payload) return

      // Tolerate either casing in case the hub's serializer is ever switched to PascalCase.
      const incomingKey = payload.projectKey ?? payload.ProjectKey
      const entityType = payload.entityType ?? payload.EntityType

      if (!incomingKey || !projectKey) return

      // Cheap and targeted: ignore anything for a project the user is not currently viewing.
      if (String(incomingKey).toLowerCase() !== String(projectKey).toLowerCase()) return

      const moduleKey = MODULE_KEY_BY_ENTITY.get(String(entityType ?? '').toLowerCase())

      // Unknown entity type (a newer server than this client): nothing to invalidate.
      if (!moduleKey) return

      scheduleInvalidation(moduleKey, projectKey)
    }

    connection.on(ENTITY_CHANGED_EVENT, handleEntityChanged)

    return () => {
      connection.off?.(ENTITY_CHANGED_EVENT, handleEntityChanged)
    }
  }, [connection, projectKey, queryClient])

  return null
}

export default ProjectRealtimeSync
