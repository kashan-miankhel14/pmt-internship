'use client'

import { useQuery } from '@tanstack/react-query'

import { projectsService } from '@/services/projects'

export const useProjectDetail = id =>
  useQuery({
    queryKey: ['project-detail', id],
    queryFn: () => projectsService.detail(id),
    enabled: Boolean(id),

    // Hold project detail data for 2 minutes — the tabs include stories/tasks/issues
    // which are invalidated by useCrudModule after any mutation, so they will auto-refetch.
    staleTime: 120000,
    gcTime: 600000
  })
