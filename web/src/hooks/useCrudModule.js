'use client'

import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import { toast } from 'react-toastify'

import { extractErrors } from '@/libs/errors'
import { useDebounce } from './useDebounce'

// After any task/story/issue mutation, also invalidate the project-detail aggregate so the
// Project Detail page tabs (Stories, Tasks, Issues) stay in sync with the board views.
const PROJECT_DETAIL_SYNC_KEYS = new Set(['tasks', 'stories', 'issues'])

// After any project/user/department mutation, also invalidate the lookups cache so that
// Kanban cards, table cells and dropdowns reflect the updated display names immediately.
const LOOKUP_SYNC_KEYS = new Set(['projects', 'users', 'departments'])

// Dashboard metric cards (total projects/tasks/issues/departments) need a refresh when
// any of these modules gains or loses a record.
const DASHBOARD_SYNC_KEYS = new Set(['tasks', 'issues', 'projects', 'departments', 'stories'])


export const useCrudModule = (key, service, params) => {
  const queryClient = useQueryClient()

  // Debounce only the search parameter so typing doesn't spam the API
  const debouncedSearch = useDebounce(params?.search, 500)
  const debouncedParams = { ...params, search: debouncedSearch }

  const listQuery = useQuery({
    queryKey: [key, debouncedParams],
    queryFn: () => service.list(debouncedParams),

    // Paging, resizing and searching all change the query key, so without this the table drops
    // to `isLoading` and unmounts every row (up to 200 on the boards) before remounting them
    // with the next payload. Keeping the previous page mounted turns that into a plain data
    // swap and removes the spinner flash between pages.
    placeholderData: keepPreviousData
  })

  const invalidate = () => {
    // Always refresh the current module's own list
    queryClient.invalidateQueries({ queryKey: [key] })

    // Sync project-detail tabs when tasks/stories/issues change
    if (PROJECT_DETAIL_SYNC_KEYS.has(key)) {
      queryClient.invalidateQueries({ queryKey: ['project-detail'] })
    }

    // Sync lookups (dropdown data, card display names) when reference data changes
    if (LOOKUP_SYNC_KEYS.has(key)) {
      queryClient.invalidateQueries({ queryKey: ['lookups'] })
    }

    // Sync dashboard metric totals when record counts change
    if (DASHBOARD_SYNC_KEYS.has(key)) {
      queryClient.invalidateQueries({ queryKey: ['dashboard'] })
    }
  }


  const handleError = error => {
    // A 403 triggers a redirect to /forbidden via AuthContext; no toast needed.
    if (error.response?.status === 403) return

    const errors = extractErrors(error.response?.data ?? { errors: [error.message] })

    toast.error(errors[0])
  }

  return {
    ...listQuery,
    createMutation: useMutation({ mutationFn: service.create, onSuccess: invalidate, onError: handleError }),
    updateMutation: useMutation({ mutationFn: ({ id, body }) => service.update(id, body), onSuccess: invalidate, onError: handleError }),
    deleteMutation: useMutation({ mutationFn: service.remove, onSuccess: invalidate, onError: handleError })
  }
}
