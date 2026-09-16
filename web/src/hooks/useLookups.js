'use client'

import { useQuery } from '@tanstack/react-query'

import { lookupsService } from '@/services/lookups'

/**
 * `options` is an escape hatch for consumers that only want to *read* the shared lookups cache
 * (e.g. the realtime project-group sync resolving projectId -> projectKey): passing
 * `{ enabled: false }` still returns whatever the page-level callers already fetched, without
 * adding a request of its own. Called with no arguments the behaviour is unchanged.
 */
export const useLookups = (options = {}) =>
  useQuery({
    queryKey: ['lookups'],
    queryFn: () => lookupsService.list(),

    // One `list()` fans out to six paged endpoints (departments, users, projects, stories,
    // tasks, roles) at pageSize 200. Every table, board and detail page calls this hook, so at
    // the default 30s staleTime a user clicking through the nav re-issues all six requests on
    // each page. Dropdown reference data barely moves, so it is held for five minutes and kept
    // in cache for thirty.
    staleTime: 300000,
    gcTime: 1800000,
    ...options
  })
