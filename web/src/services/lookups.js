import { endpoints } from '@/api/endpoints'
import { httpGet } from '@/api/httpClient'
import { entityTypes, gitProviders } from '@/libs/enums'
import { MAX_PAGE_SIZE } from '@/services/crud'

/**
 * Dropdown data. Each endpoint is permission-gated independently, so a 403 on one
 * lookup degrades that dropdown to empty rather than failing the whole screen.
 *
 * `pageSize` defaults to the API cap (200) but can be trimmed per collection: small, stable
 * reference lists (e.g. departments) never need the full 200-row page for a dropdown.
 */
const safeItems = (path, { pageSize = MAX_PAGE_SIZE, params } = {}) =>
  httpGet(path, { params: { page: 1, pageSize, ...params }, __suppressForbidden: true })
    .then(data => data.items ?? [])
    .catch(() => [])

const safeCollection = path => httpGet(path, { __suppressForbidden: true }).then(data => data ?? []).catch(() => [])

// Departments are a small, slow-moving reference list, so a dropdown never needs the full
// 200-row page. Roles come back from a non-paged endpoint (safeCollection) already.
const DEPARTMENTS_PAGE_SIZE = 100

export const lookupsService = {
  list: async () => {
    const [departments, users, projects, stories, tasks, roles, teams] = await Promise.all([
      safeItems(endpoints.departments, { pageSize: DEPARTMENTS_PAGE_SIZE }),
      safeItems(endpoints.users.base),
      safeItems(endpoints.projects),
      safeItems(endpoints.userStories),
      safeItems(endpoints.tasks),
      safeCollection(endpoints.users.availableRoles),
      safeItems(endpoints.teams)
    ])

    return { departments, users, projects, stories, tasks, roles, teams, gitProviders, entityTypes }
  }
}
