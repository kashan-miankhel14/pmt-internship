'use client'

import { useQuery } from '@tanstack/react-query'

import { reportsService } from '@/services/reports'

export const useDashboard = () =>
  useQuery({
    queryKey: ['dashboard'],
    queryFn: () => reportsService.overview(),

    // Dashboard data (velocity charts, totals) is expensive to compute but changes slowly.
    // Hold it for 3 minutes so rapid page switches don't hammer the reports endpoints.
    staleTime: 180000,
    gcTime: 600000
  })
