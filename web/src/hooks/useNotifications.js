'use client'

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import { notificationsService } from '@/services/notifications'
import { useAbility } from '@/contexts/AbilityContext'

export const useNotifications = () => {
  const queryClient = useQueryClient()
  const ability = useAbility()

  // NotificationsController requires `notifications.manage` for reads as well as writes,
  // so roles without it must not fire the request at all.
  const canRead = ability.can('manage', 'notifications')

  const listQuery = useQuery({
    queryKey: ['notifications'],
    queryFn: () => notificationsService.listMine(),
    enabled: canRead,
    staleTime: 120_000
  })

  const markReadMutation = useMutation({
    mutationFn: id => notificationsService.markRead(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['notifications'] })
  })

  const markAllReadMutation = useMutation({
    mutationFn: async unreadIds => {
      await Promise.all(unreadIds.map(id => notificationsService.markRead(id)))
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['notifications'] })
  })

  return { ...listQuery, canRead, markReadMutation, markAllReadMutation }
}
