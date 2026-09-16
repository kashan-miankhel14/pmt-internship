'use client'

import { useEffect, useState } from 'react'

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { toast } from 'react-toastify'

import Button from '@mui/material/Button'
import CircularProgress from '@mui/material/CircularProgress'
import Dialog from '@mui/material/Dialog'
import DialogActions from '@mui/material/DialogActions'
import DialogContent from '@mui/material/DialogContent'
import DialogTitle from '@mui/material/DialogTitle'
import Tooltip from '@mui/material/Tooltip'
import Typography from '@mui/material/Typography'

import { aiAgentService } from '@/services/aiAgent'
import { aiErrorMessage, formatTimeSpan, isAiAdmin } from '@/libs/aiChat'
import { useAuth } from '@/contexts/AuthContext'

/**
 * Rebuilds the retrieval index (`POST /ai/agent/admin/reindex`).
 *
 * Behaviour dictated by the API:
 *  - Gated on the `Administrator` *role*, not a permission, so the visibility test reads the
 *    role claim. Hiding the button is only a UI hint; the endpoint is the real authority.
 *  - Answers 202 immediately because a full rebuild embeds every indexable entity and would
 *    outlive the request, so progress has to be polled from the status endpoint.
 *  - Answers 409 when a rebuild is already running (single-flight, never queued). That is an
 *    expected outcome, not an error: start polling instead of showing a failure.
 */

const POLL_INTERVAL_MS = 3000

const AiReindexButton = () => {
  const { session } = useAuth()
  const queryClient = useQueryClient()
  const [confirmOpen, setConfirmOpen] = useState(false)
  const [polling, setPolling] = useState(false)

  const isAdmin = isAiAdmin(session)

  const statusQuery = useQuery({
    queryKey: ['ai', 'reindex-status'],
    queryFn: () => aiAgentService.getReindexStatus(),
    enabled: isAdmin && polling,
    refetchInterval: polling ? POLL_INTERVAL_MS : false,
    retry: false
  })

  const status = statusQuery.data

  /**
   * Drops the previous run's snapshot before polling resumes. Without this react-query serves
   * the cached `isRunning: false` from the *last* rebuild on the first render of the new one,
   * and the completion effect below would announce a result that has not happened yet.
   */
  const startPolling = () => {
    queryClient.removeQueries({ queryKey: ['ai', 'reindex-status'] })
    setPolling(true)
  }

  // Stop polling once the worker reports it is finished, then report the outcome once.
  useEffect(() => {
    if (!polling || !status || status.isRunning) return

    setPolling(false)

    if (status.lastError) {
      toast.error(`Reindex failed: ${status.lastError}`)

      return
    }

    const result = status.lastResult

    toast.success(
      result
        ? `Reindex finished: ${result.indexed} indexed, ${result.skipped} skipped, ${result.failed} failed in ${formatTimeSpan(result.duration)}.`
        : 'Reindex finished.'
    )
  }, [polling, status])

  const reindexMutation = useMutation({
    mutationFn: () => aiAgentService.reindex(),
    onSuccess: () => {
      toast.info('Rebuilding the AI index. This runs in the background.')
      startPolling()
    },
    onError: error => {
      // 409 is the single-flight guard, not a failure: a rebuild is already under way, so
      // follow that one instead of reporting an error.
      if (error.response?.status === 409) {
        toast.info('A rebuild is already running.')
        startPolling()

        return
      }

      toast.error(aiErrorMessage(error))
    }
  })

  if (!isAdmin) return null

  const running = polling || reindexMutation.isPending

  return (
    <>
      <Tooltip title={running ? 'Rebuild in progress' : 'Rebuild the AI retrieval index'}>
        <span>
          <Button
            size='small'
            variant='tonal'
            color='secondary'
            disabled={running}
            onClick={() => setConfirmOpen(true)}
            startIcon={running ? <CircularProgress size={14} /> : <i className='tabler-refresh' />}
          >
            {running ? 'Indexing' : 'Reindex'}
          </Button>
        </span>
      </Tooltip>

      <Dialog open={confirmOpen} onClose={() => setConfirmOpen(false)} fullWidth maxWidth='xs'>
        <DialogTitle>Rebuild the AI index?</DialogTitle>
        <DialogContent>
          <Typography color='text.secondary'>
            Every project, story, task, issue and comment is re-embedded. This runs in the background and can take
            several minutes; the assistant keeps answering from the current index until it finishes.
          </Typography>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setConfirmOpen(false)}>Cancel</Button>
          <Button
            variant='contained'
            disabled={reindexMutation.isPending}
            onClick={() => {
              setConfirmOpen(false)
              reindexMutation.mutate()
            }}
          >
            Rebuild
          </Button>
        </DialogActions>
      </Dialog>
    </>
  )
}

export default AiReindexButton
