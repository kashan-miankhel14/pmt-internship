'use client'

import { useState } from 'react'

import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import CircularProgress from '@mui/material/CircularProgress'
import Dialog from '@mui/material/Dialog'
import DialogActions from '@mui/material/DialogActions'
import DialogContent from '@mui/material/DialogContent'
import DialogTitle from '@mui/material/DialogTitle'
import IconButton from '@mui/material/IconButton'
import List from '@mui/material/List'
import ListItemButton from '@mui/material/ListItemButton'
import Tooltip from '@mui/material/Tooltip'
import Typography from '@mui/material/Typography'

import { errorMessage } from '@/components/pmt/ErrorState'
import { formatRelativeTime } from '@/libs/aiChat'

/**
 * Conversation switcher.
 *
 * Sessions are private to the signed-in user (the API answers 404 for anybody else's id), so
 * this list is the complete history available to the panel. Deletion is a soft delete on the
 * server but irreversible from the UI, hence the confirmation step.
 */
const AiChatSidebar = ({
  sessions,
  sessionsQuery,
  activeSessionId,
  onSelect,
  onNewSession,
  onDelete,
  isDeleting,
  fullWidth = false
}) => {
  const [pendingDelete, setPendingDelete] = useState(null)

  const confirmDelete = () => {
    if (!pendingDelete) return

    onDelete(pendingDelete.id)
    setPendingDelete(null)
  }

  return (
    <Box
      sx={{
        inlineSize: fullWidth ? '100%' : 240,
        flexShrink: 0,
        display: 'flex',
        flexDirection: 'column',
        borderInlineEnd: fullWidth ? 0 : '1px solid',
        borderColor: 'divider',
        bgcolor: 'action.hover'
      }}
    >
      <Box sx={{ p: 3 }}>
        <Button
          fullWidth
          size='small'
          variant='contained'
          startIcon={<i className='tabler-plus' />}
          onClick={onNewSession}
        >
          New chat
        </Button>
      </Box>

      <Box sx={{ flex: 1, overflowY: 'auto' }}>
        {sessionsQuery.isLoading ? (
          <Box sx={{ display: 'flex', justifyContent: 'center', p: 4 }}>
            <CircularProgress size={20} />
          </Box>
        ) : sessionsQuery.isError ? (
          <Box sx={{ p: 3, display: 'flex', flexDirection: 'column', gap: 2 }}>
            <Typography variant='caption' color='error'>
              {errorMessage(sessionsQuery.error)}
            </Typography>
            <Button size='small' variant='tonal' color='error' onClick={() => sessionsQuery.refetch()}>
              Try again
            </Button>
          </Box>
        ) : sessions.length ? (
          <List disablePadding>
            {sessions.map(session => (
              <ListItemButton
                key={session.id}
                selected={session.id === activeSessionId}
                onClick={() => onSelect(session.id)}
                sx={{ alignItems: 'flex-start', gap: 1, py: 2 }}
              >
                <Box sx={{ flex: 1, minInlineSize: 0 }}>
                  <Typography
                    variant='body2'
                    sx={{
                      fontWeight: session.id === activeSessionId ? 600 : 400,
                      overflow: 'hidden',
                      textOverflow: 'ellipsis',
                      whiteSpace: 'nowrap'
                    }}
                  >
                    {session.title || 'New conversation'}
                  </Typography>
                  <Typography variant='caption' color='text.secondary'>
                    {formatRelativeTime(session.updatedAt)}
                    {session.messageCount ? ` - ${session.messageCount} messages` : ''}
                  </Typography>
                </Box>
                <Tooltip title='Delete conversation'>
                  <IconButton
                    size='small'
                    aria-label='Delete conversation'
                    onClick={event => {
                      // The row is a button; without this the click also switches to the
                      // session that is about to be deleted.
                      event.stopPropagation()
                      setPendingDelete(session)
                    }}
                  >
                    <i className='tabler-trash' style={{ fontSize: '1rem' }} />
                  </IconButton>
                </Tooltip>
              </ListItemButton>
            ))}
          </List>
        ) : (
          <Typography variant='caption' color='text.secondary' sx={{ display: 'block', p: 3 }}>
            No conversations yet. Ask Anna something to start one.
          </Typography>
        )}
      </Box>

      <Dialog open={Boolean(pendingDelete)} onClose={() => setPendingDelete(null)} fullWidth maxWidth='xs'>
        <DialogTitle>Delete conversation?</DialogTitle>
        <DialogContent>
          <Typography color='text.secondary'>
            &quot;{pendingDelete?.title || 'New conversation'}&quot; and its messages will be removed from your
            history. This cannot be undone.
          </Typography>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setPendingDelete(null)}>Cancel</Button>
          <Button color='error' variant='contained' onClick={confirmDelete} disabled={isDeleting}>
            Delete
          </Button>
        </DialogActions>
      </Dialog>
    </Box>
  )
}

export default AiChatSidebar
