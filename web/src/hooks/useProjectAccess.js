'use client'

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import { toast } from 'react-toastify'

import { extractErrors } from '@/libs/errors'
import { fallbackProjectRoles } from '@/libs/projectTemplates'
import { projectAccessService } from '@/services/projectAccess'
import { projectsService } from '@/services/projects'

const ACCESS_KEY = 'project-access'

const handleError = error => {
  if (error.response?.status === 403) return

  toast.error(extractErrors(error.response?.data ?? { errors: [error.message] })[0])
}

// Collections are returned either bare or wrapped in the paged envelope; both are accepted.
const toItems = data => (Array.isArray(data) ? data : (data?.items ?? []))

/**
 * Seeded project roles (Project Admin > Member > Viewer). A missing or forbidden
 * `/project-roles` endpoint degrades to the seeded list rather than an empty dropdown, because
 * a grant cannot be created without a role.
 */
export const useProjectRoles = () => {
  const query = useQuery({
    queryKey: ['project-roles'],
    queryFn: () => projectAccessService.listRoles({ page: 1, pageSize: 50 }),

    // Reference data that only changes on a deployment.
    staleTime: 300000,
    gcTime: 1800000,
    retry: false
  })

  const roles = toItems(query.data)

  return { ...query, roles: roles.length ? roles : fallbackProjectRoles }
}

/**
 * Resolves the project key an access route needs from whatever the URL carries. The dashboard
 * routes are `/projects/[id]`, while the access endpoints are keyed by `Projects.Key`, so the
 * project record is loaded once and its key reused for every grant call.
 */
export const useProjectKey = idOrKey => {
  const isNumericId = /^\d+$/.test(String(idOrKey ?? ''))

  const query = useQuery({
    queryKey: ['project', idOrKey],
    queryFn: () => projectsService.get(idOrKey),
    enabled: Boolean(idOrKey)
  })

  return {
    project: query.data ?? null,
    projectKey: query.data?.key ?? (isNumericId ? null : String(idOrKey ?? '')),
    isLoading: query.isLoading,
    isError: query.isError,
    error: query.error
  }
}

/**
 * Stage 4 access management for one project: direct member grants and team grants, plus the
 * mutations behind the settings screen. Both collections are invalidated together because the
 * effective role of a user can change through either path.
 */
export const useProjectAccess = projectKey => {
  const queryClient = useQueryClient()
  const enabled = Boolean(projectKey)

  const membersQuery = useQuery({
    queryKey: [ACCESS_KEY, projectKey, 'members'],
    queryFn: () => projectAccessService.listMembers(projectKey, { page: 1, pageSize: 200 }),
    enabled
  })

  const teamsQuery = useQuery({
    queryKey: [ACCESS_KEY, projectKey, 'teams'],
    queryFn: () => projectAccessService.listTeams(projectKey, { page: 1, pageSize: 200 }),
    enabled
  })

  const invalidate = () => queryClient.invalidateQueries({ queryKey: [ACCESS_KEY, projectKey] })

  const mutation = mutationFn => ({ mutationFn, onSuccess: invalidate, onError: handleError })

  return {
    members: toItems(membersQuery.data),
    teams: toItems(teamsQuery.data),
    membersQuery,
    teamsQuery,
    isLoading: membersQuery.isLoading || teamsQuery.isLoading,

    addMemberMutation: useMutation(mutation(data => projectAccessService.addMember(projectKey, data))),
    setMemberRoleMutation: useMutation(
      mutation(({ userId, projectRoleId }) => projectAccessService.setMemberRole(projectKey, userId, projectRoleId))
    ),
    removeMemberMutation: useMutation(mutation(userId => projectAccessService.removeMember(projectKey, userId))),

    addTeamMutation: useMutation(mutation(data => projectAccessService.addTeam(projectKey, data))),
    setTeamRoleMutation: useMutation(
      mutation(({ teamId, projectRoleId }) => projectAccessService.setTeamRole(projectKey, teamId, projectRoleId))
    ),
    removeTeamMutation: useMutation(mutation(teamId => projectAccessService.removeTeam(projectKey, teamId)))
  }
}
