'use client'

import { useMemo } from 'react'

import { usePathname, useSearchParams } from 'next/navigation'

import { useLookups } from '@/hooks/useLookups'

/**
 * `/projects/5`, `/projects/5/settings/access`, ... — the segment is the NUMERIC project id, not
 * the key. `/projects` and `/projects/new` deliberately fall through (no digits to capture).
 */
const PROJECT_ROUTE_PATTERN = /^\/projects\/(\d+)(?:\/|$)/

// Routes whose project context lives in the `?projectId=` query param instead of the path.
const PROJECT_PARAM_ROUTES = ['/tasks', '/stories', '/issues']

const toProjectId = value => {
  const parsed = Number(value)

  return Number.isInteger(parsed) && parsed > 0 ? parsed : null
}

/**
 * The numeric id of the project the user is currently looking at, or null when the route has no
 * project in context (dashboard, users, departments, the "All projects" board filter, ...).
 */
export const useCurrentProjectId = () => {
  const pathname = usePathname() ?? ''
  const searchParams = useSearchParams()

  // Read the primitive out here: `searchParams` is a new object identity on every navigation,
  // so memoising on it directly would recompute on unrelated query-string changes.
  const paramProjectId = searchParams?.get('projectId') ?? null

  return useMemo(() => {
    const routeMatch = PROJECT_ROUTE_PATTERN.exec(pathname)

    // The route segment wins: on a project workspace page the query string is irrelevant.
    if (routeMatch) return toProjectId(routeMatch[1])

    const isParamRoute = PROJECT_PARAM_ROUTES.some(route => pathname === route || pathname.startsWith(`${route}/`))

    if (!isParamRoute) return null

    return toProjectId(paramProjectId)
  }, [pathname, paramProjectId])
}

/**
 * The `Projects.Key` of the project in context, resolved from the numeric id in the URL through
 * the projects lookup. The backend groups realtime traffic by key ("project:{projectKey}") while
 * the app routes on the id, so this is the bridge between the two.
 *
 * Returns null when no project is in context, or while the lookups cache has not resolved yet —
 * callers treat null as "not scoped to any project".
 */
export const useCurrentProjectKey = () => {
  const projectId = useCurrentProjectId()

  // Shares the ['lookups'] cache every board/table/detail page already populates. `enabled` is
  // false on routes without a project so this never adds a request of its own; react-query still
  // hands back the cached payload when another consumer has fetched it.
  const { data: lookups } = useLookups({ enabled: projectId !== null })

  return useMemo(() => {
    if (projectId === null) return null

    return (lookups?.projects ?? []).find(project => project.id === projectId)?.key ?? null
  }, [lookups?.projects, projectId])
}
