'use client'

import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import { toast } from 'react-toastify'

import { extractErrors } from '@/libs/errors'
import { teamsService } from '@/services/teams'
import { useDebounce } from './useDebounce'

const TEAMS_KEY = 'teams'

// Same contract as useCrudModule: a 403 is already handled by the AuthContext redirect, every
// other failure surfaces as a single toast line instead of an unhandled rejection.
const handleError = error => {
  if (error.response?.status === 403) return

  toast.error(extractErrors(error.response?.data ?? { errors: [error.message] })[0])
}

/**
 * Team registry: paged list plus the create/update/delete mutations for the list screen.
 * Mirrors useCrudModule (debounced search, keepPreviousData paging) but is typed to the team
 * service so member management can live next to it.
 */
export const useTeams = params => {
  const queryClient = useQueryClient()

  const debouncedSearch = useDebounce(params?.search, 500)
  const debouncedParams = { ...params, search: debouncedSearch }

  const listQuery = useQuery({
    queryKey: [TEAMS_KEY, debouncedParams],
    queryFn: () => teamsService.list(debouncedParams),

    // Paging and searching change the query key; without this the table unmounts every row
    // and flashes a spinner between pages.
    placeholderData: keepPreviousData
  })

  const invalidate = () => queryClient.invalidateQueries({ queryKey: [TEAMS_KEY] })

  return {
    ...listQuery,
    items: listQuery.data?.items ?? [],
    totalCount: listQuery.data?.totalCount ?? 0,
    createMutation: useMutation({ mutationFn: teamsService.create, onSuccess: invalidate, onError: handleError }),
    updateMutation: useMutation({
      mutationFn: ({ id, body }) => teamsService.update(id, body),
      onSuccess: invalidate,
      onError: handleError
    }),
    deleteMutation: useMutation({ mutationFn: teamsService.remove, onSuccess: invalidate, onError: handleError })
  }
}

/**
 * One team plus its members, with the member-management mutations used by /teams/[id].
 * Every mutation invalidates both the detail and the registry, because the list shows the
 * member count and the lead.
 */
export const useTeam = teamId => {
  const queryClient = useQueryClient()

  const detailQuery = useQuery({
    queryKey: [TEAMS_KEY, 'detail', teamId],
    queryFn: () => teamsService.detail(teamId),
    enabled: Boolean(teamId)
  })

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: [TEAMS_KEY, 'detail', teamId] })
    queryClient.invalidateQueries({ queryKey: [TEAMS_KEY] })
  }

  const mutation = mutationFn => ({ mutationFn, onSuccess: invalidate, onError: handleError })

  return {
    ...detailQuery,
    team: detailQuery.data ?? null,
    members: detailQuery.data?.members ?? [],
    updateMutation: useMutation(mutation(body => teamsService.update(teamId, body))),

    // Bulk add: the picker hands over every selected user in one request.
    addMembersMutation: useMutation(mutation(data => teamsService.addMembers(teamId, data))),
    setMemberRoleMutation: useMutation(
      mutation(({ userId, teamRole }) => teamsService.setMemberRole(teamId, userId, teamRole))
    ),
    removeMemberMutation: useMutation(mutation(userId => teamsService.removeMember(teamId, userId)))
  }
}
