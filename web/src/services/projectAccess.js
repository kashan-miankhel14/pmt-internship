import { endpoints } from '@/api/endpoints'
import { httpDelete, httpGet, httpPost } from '@/api/httpClient'
import { optionalId, optionalText, requiredText } from '@/libs/payload'

const projectPath = projectKey => `${endpoints.projects}/${projectKey}`

// Grants are keyed on (ProjectId, UserId) / (ProjectId, TeamId), so a POST for a principal that
// already holds a grant is an upsert. That is what an inline role change posts.
const toMembersRequest = ({ userIds, userId, projectRoleId } = {}) => ({
  userIds: (userIds ?? [userId]).filter(value => value != null).map(Number),
  projectRoleId: optionalId(projectRoleId)
})

const toTeamRequest = ({ teamId, projectRoleId } = {}) => ({
  teamId: optionalId(teamId),
  projectRoleId: optionalId(projectRoleId)
})

/*
  CreateProjectFromTemplateRequest. `templateCode` and `typeCode` carry the same
  SCRUM | KANBAN | BASIC value: the wizard calls it a template, Projects.TypeCode stores it.
  Both are sent so the request binds whichever name the API settles on.
*/
const toCreateRequest = body => {
  const templateCode = requiredText(body.templateCode ?? body.typeCode).toUpperCase()

  return {
    key: requiredText(body.key).toUpperCase(),
    name: requiredText(body.name),
    description: optionalText(body.description),
    templateCode,
    typeCode: templateCode,
    accessLevel: requiredText(body.accessLevel || 'RESTRICTED').toUpperCase(),
    leadUserId: optionalId(body.leadUserId)
  }
}

export const projectAccessService = {
  // Direct grants: ProjectMembers(ProjectId, UserId, ProjectRoleId)
  listMembers: (projectKey, params = {}) => httpGet(`${projectPath(projectKey)}/members`, { params }),
  addMember: (projectKey, data) => httpPost(`${projectPath(projectKey)}/members`, toMembersRequest(data)),
  setMemberRole: (projectKey, userId, projectRoleId) =>
    httpPost(`${projectPath(projectKey)}/members`, toMembersRequest({ userId, projectRoleId })),
  removeMember: (projectKey, userId) => httpDelete(`${projectPath(projectKey)}/members/${userId}`),

  // Team grants: every TeamMember inherits the role granted to the team.
  listTeams: (projectKey, params = {}) => httpGet(`${projectPath(projectKey)}/teams`, { params }),
  addTeam: (projectKey, data) => httpPost(`${projectPath(projectKey)}/teams`, toTeamRequest(data)),
  setTeamRole: (projectKey, teamId, projectRoleId) =>
    httpPost(`${projectPath(projectKey)}/teams`, toTeamRequest({ teamId, projectRoleId })),
  removeTeam: (projectKey, teamId) => httpDelete(`${projectPath(projectKey)}/teams/${teamId}`),

  /**
   * Project creation wizard (stage 3). The template expansion — counters, default board,
   * columns, issue types, creator/lead memberships — happens server-side in one transaction.
   *
   * Section 11 of the flow spec folds this into `POST /projects`; builds that expose the
   * dedicated `/projects/from-template` route are tried first, and a missing-route answer
   * (404/405) falls back to the plain collection POST rather than failing the wizard.
   */
  createFromTemplate: async data => {
    const body = toCreateRequest(data)

    try {
      return await httpPost(`${endpoints.projects}/from-template`, body)
    } catch (error) {
      if (![404, 405].includes(error.response?.status)) throw error

      return httpPost(endpoints.projects, body)
    }
  },

  // Seeded roles: Project Admin (SortOrder 1) > Member > Viewer. Reference data for a
  // dropdown, so a 403 leaves the caller on its fallback list instead of routing to /forbidden.
  listRoles: (params = {}) => httpGet(endpoints.projectRoles, { params, __suppressForbidden: true })
}
