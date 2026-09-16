import { endpoints } from '@/api/endpoints'
import { httpDelete, httpGet, httpPatch, httpPost, httpPut } from '@/api/httpClient'
import { optionalId, optionalText, requiredText, toBool } from '@/libs/payload'
import { MAX_PAGE_SIZE } from '@/services/crud'

// UpsertTeamRequest(Key, Name, Description, LeadUserId, Active)
// Team.Key is CHECK-constrained to A-Z0-9, so it is normalised before it leaves the browser.
const toRequest = body => ({
  key: requiredText(body.key).toUpperCase(),
  name: requiredText(body.name),
  description: optionalText(body.description),
  leadUserId: optionalId(body.leadUserId),
  active: toBool(body.active)
})

const toListParams = ({ page = 1, pageSize = 25, search } = {}) => {
  const params = { page, pageSize: Math.min(pageSize, MAX_PAGE_SIZE) }

  if (search) params.search = search

  return params
}

// Members can be sent one at a time or in bulk; the API takes a userIds array either way.
const toMembersRequest = ({ userIds, userId, teamRole = 'Member' } = {}) => ({
  userIds: (userIds ?? [userId]).filter(value => value != null).map(Number),
  teamRole
})

export const teamsService = {
  list: (params = {}) => httpGet(endpoints.teams, { params: toListParams(params) }),

  /**
   * Typeahead read used by TeamPicker. Suppresses the 403 redirect (and swallows a missing
   * endpoint) so a dialog on another screen degrades to an empty dropdown instead of routing
   * the user away mid-task.
   */
  search: (params = {}) =>
    httpGet(endpoints.teams, { params: toListParams(params), __suppressForbidden: true }).catch(() => ({ items: [] })),

  get: id => httpGet(`${endpoints.teams}/${id}`),
  create: body => httpPost(endpoints.teams, toRequest(body)),
  update: (id, body) => httpPut(`${endpoints.teams}/${id}`, toRequest(body)),
  remove: id => httpDelete(`${endpoints.teams}/${id}`),

  // Team members. `GET /teams/{id}` answers with detail + members, so the sub-resource read
  // is only a fallback for payloads that omit the collection (see teamsService.detail).
  listMembers: (teamId, params = {}) => httpGet(`${endpoints.teams}/${teamId}/members`, { params }),
  addMembers: (teamId, data) => httpPost(`${endpoints.teams}/${teamId}/members`, toMembersRequest(data)),
  setMemberRole: (teamId, userId, teamRole) =>
    httpPatch(`${endpoints.teams}/${teamId}/members/${userId}`, { teamRole }),
  removeMember: (teamId, userId) => httpDelete(`${endpoints.teams}/${teamId}/members/${userId}`)
}

/**
 * Team detail normalised for the detail screen: always `{ ...team, members: [] }`, whether the
 * API embeds the collection in the aggregate or exposes it only as a sub-resource.
 */
teamsService.detail = async id => {
  const team = await teamsService.get(id)

  if (Array.isArray(team?.members)) return team

  const members = await teamsService.listMembers(id).catch(() => [])

  return { ...team, members: Array.isArray(members) ? members : (members?.items ?? []) }
}
