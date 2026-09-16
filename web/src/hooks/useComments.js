'use client'

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import { commentsService } from '@/services/comments'
import { useAbility } from '@/contexts/AbilityContext'

export const useComments = ({ entityType, entityId, enabled = true }) => {
  const queryClient = useQueryClient()
  const ability = useAbility()

  // CommentsController is gated by `comments.manage` for reads too, so a role without it
  // must not issue the request (it would 403 and bounce the page to /forbidden).
  const canManage = ability.can('manage', 'comments')
  const queryKey = ['comments', entityType, entityId]

  const listQuery = useQuery({
    queryKey,
    queryFn: () => commentsService.list({ entityType, entityId }),
    enabled: enabled && canManage && Boolean(entityType && entityId)
  })

  const createMutation = useMutation({
    mutationFn: body => commentsService.create(body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey })
  })

  const deleteMutation = useMutation({
    mutationFn: id => commentsService.remove(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey })
  })

  const updateMutation = useMutation({
    mutationFn: body => commentsService.update(body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey })
  })

  return { ...listQuery, canManage, createMutation, updateMutation, deleteMutation }
}
