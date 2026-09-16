'use client'

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import { toast } from 'react-toastify'

import { extractErrors } from '@/libs/errors'
import { sprintsService } from '@/services/sprints'

const SPRINTS_KEY = 'sprints'

// Same contract as useCrudModule/useProjectAccess: a 403 is handled by the AuthContext redirect,
// every other failure surfaces as a single toast line.
const handleError = error => {
  if (error.response?.status === 403) return

  toast.error(extractErrors(error.response?.data ?? { errors: [error.message] })[0])
}

// Sprints come back either bare or wrapped in the paged envelope; both are accepted.
const toItems = data => (Array.isArray(data) ? data : (data?.items ?? []))

/**
 * Sprints for one project (keyed by Projects.Key) plus the create / update / start / complete /
 * remove mutations behind the Sprints tab. Every mutation invalidates the project's sprint list.
 */
export const useSprints = projectKey => {
  const queryClient = useQueryClient()
  const enabled = Boolean(projectKey)

  const listQuery = useQuery({
    queryKey: [SPRINTS_KEY, projectKey],
    queryFn: () => sprintsService.listSprints(projectKey),
    enabled
  })

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: [SPRINTS_KEY, projectKey] })
    // Keep project-detail in sync so the Sprints tab reflects the new sprint state
    queryClient.invalidateQueries({ queryKey: ['project-detail'] })
  }

  const mutation = mutationFn => ({ mutationFn, onSuccess: invalidate, onError: handleError })

  return {
    ...listQuery,
    sprints: toItems(listQuery.data),
    createMutation: useMutation(mutation(data => sprintsService.createSprint(projectKey, data))),
    updateMutation: useMutation(mutation(({ id, body }) => sprintsService.updateSprint(projectKey, id, body))),
    startMutation: useMutation(mutation(id => sprintsService.startSprint(projectKey, id))),
    completeMutation: useMutation(mutation(id => sprintsService.completeSprint(projectKey, id))),
    removeMutation: useMutation(mutation(id => sprintsService.removeSprint(projectKey, id)))
  }
}
