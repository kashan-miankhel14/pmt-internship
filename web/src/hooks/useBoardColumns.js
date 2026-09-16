'use client'

import { useMemo } from 'react'

import { useQuery } from '@tanstack/react-query'

import { boardService } from '@/services/board'

/**
 * Turns a column display name into the status enum value a card carries. The seeded board
 * columns are spaced ("To Do", "In Progress") while Task/Story.Status is a compact enum
 * ("ToDo", "InProgress"), so stripping every non-alphanumeric character reconciles the two.
 * Enum values passed straight through (the board fallbacks) normalise to themselves.
 */
const toStatus = name => String(name ?? '').replace(/[^a-zA-Z0-9]/g, '')

// Template-seeded board columns, used as the fallback while the API is loading or unavailable
// but a project *is* selected. Mirrors the seed described in the flow spec.
const DEFAULT_COLUMN_NAMES = {
  SCRUM: ['To Do', 'In Progress', 'Blocked', 'Review', 'Done'],
  BASIC: ['To Do', 'In Progress', 'Blocked', 'Review', 'Done'],
  KANBAN: ['Backlog', 'Selected', 'In Progress', 'Review', 'Done']
}

// Normalises whatever the API returns (paged envelope or bare array) and the various field
// name casings a backend might settle on (Name/name, Ordinal/ordinal, CompleteColumn/...).
const toColumn = (raw, index) => {
  const name = raw?.name ?? raw?.Name ?? ''

  return {
    id: raw?.id ?? raw?.Id ?? null,
    name,
    status: toStatus(name),
    ordinal: raw?.ordinal ?? raw?.Ordinal ?? index,
    completeColumn: Boolean(raw?.completeColumn ?? raw?.CompleteColumn ?? false)
  }
}

// A column list built from bare names (fallbacks). The last one is treated as the "done"
// column so downstream consumers still have a completion flag to lean on.
const columnsFromNames = names =>
  names.map((name, index) => ({
    id: null,
    name,
    status: toStatus(name),
    ordinal: index,
    completeColumn: index === names.length - 1
  }))

const toItems = data => (Array.isArray(data) ? data : (data?.items ?? []))

/**
 * Board columns for one project, config-driven with graceful fallbacks so the board always has
 * something to render.
 *
 * - No `projectKey` (e.g. the global boards with the "All projects" filter): returns the caller's
 *   hardcoded `fallback` statuses, preserving the previous behaviour.
 * - `projectKey` present but the API is still loading / errored / returned nothing: returns the
 *   template defaults (by `templateCode`), or `fallback` when the template is unknown.
 * - API responded: returns its columns, sorted by ordinal.
 *
 * `fallback` should be a stable (module-level) array of status enum values so the memoised
 * result keeps a steady identity across renders.
 */
export const useBoardColumns = ({ projectKey, templateCode, fallback = [] } = {}) => {
  const enabled = Boolean(projectKey)

  const query = useQuery({
    queryKey: ['board-columns', projectKey],
    queryFn: () => boardService.listColumns(projectKey),
    enabled,

    // Board layout barely moves between visits; hold it like the other reference lookups.
    staleTime: 300000,
    gcTime: 1800000,
    retry: false
  })

  const columns = useMemo(() => {
    const template = String(templateCode ?? '').toUpperCase()
    const defaults = DEFAULT_COLUMN_NAMES[template] ?? fallback

    // No project in context: keep the caller's hardcoded columns.
    if (!enabled) return columnsFromNames(fallback)

    const apiColumns = toItems(query.data)

    if (apiColumns.length) {
      return apiColumns
        .map(toColumn)
        .filter(column => column.status)
        .sort((a, b) => a.ordinal - b.ordinal)
    }

    // Project selected but nothing usable came back yet: template defaults keep the board alive.
    return columnsFromNames(defaults)
  }, [enabled, query.data, templateCode, fallback])

  return {
    columns,
    isLoading: query.isLoading,
    isError: query.isError,
    error: query.error,
    refetch: query.refetch
  }
}
