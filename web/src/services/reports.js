import { endpoints } from '@/api/endpoints'
import { httpGet } from '@/api/httpClient'

// SP_REPORT buckets velocity by month and filters on UpdateDate >= @From AND < @To.
// Both bounds are required: a null @From matches no rows at all.
const DEFAULT_MONTHS = 6

export const defaultVelocityRange = (months = DEFAULT_MONTHS) => {
  const now = new Date()
  const to = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() + 1, 1))
  const from = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() - (months - 1), 1))

  return { from: from.toISOString(), to: to.toISOString() }
}

/**
 * Only a permission failure is swallowed: a viewer without `reports.view` (or without a
 * module's `*.view`) should see that panel degrade to empty, while a network error or a
 * 500 must still reject so the dashboard can render a real error state instead of
 * silently showing zeroes.
 */
const isForbidden = error => error?.response?.status === 403

const safe = (promise, fallback) =>
  promise.then(data => data ?? fallback).catch(error => {
    if (isForbidden(error)) return fallback

    throw error
  })

const totalCount = path =>
  httpGet(path, { params: { page: 1, pageSize: 1 }, __suppressForbidden: true })
    .then(data => data.totalCount ?? 0)
    .catch(error => {
      if (isForbidden(error)) return null

      throw error
    })

export const reportsService = {
  velocity: ({ projectId, from, to } = {}) => {
    const range = from && to ? { from, to } : defaultVelocityRange()

    return httpGet(endpoints.reports.velocity, { params: { projectId: projectId || undefined, ...range } })
  },

  workload: ({ projectId } = {}) =>
    httpGet(endpoints.reports.workload, { params: { projectId: projectId || undefined } }),

  /**
   * Dashboard aggregate. The API has no totals endpoint, so the stat cards read
   * totalCount off each paged list. Those lists filter on IsDeleted only, so the
   * counts are record totals rather than "open"/"active" subsets.
   */
  overview: async ({ projectId } = {}) => {
    const range = defaultVelocityRange()

    const [velocity, workload, projects, tasks, issues, departments] = await Promise.all([
      safe(httpGet(endpoints.reports.velocity, { params: { projectId: projectId || undefined, ...range }, __suppressForbidden: true }), []),
      safe(httpGet(endpoints.reports.workload, { params: { projectId: projectId || undefined }, __suppressForbidden: true }), []),
      totalCount(endpoints.projects),
      totalCount(endpoints.tasks),
      totalCount(endpoints.issues),
      totalCount(endpoints.departments)
    ])

    return { velocity, workload, totals: { projects, tasks, issues, departments } }
  }
}
