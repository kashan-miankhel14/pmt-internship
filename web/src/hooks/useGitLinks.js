'use client'

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import { gitLinksService } from '@/services/gitLinks'
import { useAbility } from '@/contexts/AbilityContext'

export const useGitLinks = ({ entityType, entityId }) => {
  const queryClient = useQueryClient()
  const ability = useAbility()

  // GitLinksController is gated by `git.manage` end-to-end: without it the read 403s,
  // so skip the request and let the caller hide the write affordances.
  const canManage = ability.can('manage', 'git')
  const queryKey = ['git-links', entityType, entityId]

  const listQuery = useQuery({
    queryKey,
    queryFn: () => gitLinksService.list({ entityType, entityId }),
    enabled: canManage && Boolean(entityType && entityId)
  })

  const invalidate = () => queryClient.invalidateQueries({ queryKey })

  return {
    ...listQuery,
    canManage,
    createMutation: useMutation({ mutationFn: gitLinksService.create, onSuccess: invalidate }),
    deleteMutation: useMutation({ mutationFn: gitLinksService.remove, onSuccess: invalidate })
  }
}
