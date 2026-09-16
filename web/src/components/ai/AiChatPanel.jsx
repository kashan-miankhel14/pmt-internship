'use client'

import { useCallback, useEffect, useRef, useState } from 'react'

import Alert from '@mui/material/Alert'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import CircularProgress from '@mui/material/CircularProgress'
import Dialog from '@mui/material/Dialog'
import DialogActions from '@mui/material/DialogActions'
import DialogContent from '@mui/material/DialogContent'
import DialogTitle from '@mui/material/DialogTitle'
import Drawer from '@mui/material/Drawer'
import Fab from '@mui/material/Fab'
import IconButton from '@mui/material/IconButton'
import Tooltip from '@mui/material/Tooltip'
import Typography from '@mui/material/Typography'
import useMediaQuery from '@mui/material/useMediaQuery'
import Zoom from '@mui/material/Zoom'
import Slide from '@mui/material/Slide'

import CustomChip from '@core/components/mui/Chip'

import AiChatComposer from '@/components/ai/AiChatComposer'
import AiChatMessage from '@/components/ai/AiChatMessage'
import AiChatSidebar from '@/components/ai/AiChatSidebar'
import AiReindexButton from '@/components/ai/AiReindexButton'
import ThinkingIndicator from '@/components/ai/ThinkingIndicator'
import { errorMessage } from '@/components/pmt/ErrorState'
import { AI_CONFIRM_ACTION, describePendingAction } from '@/libs/aiChat'
import { useAiAgent } from '@/hooks/useAiAgent'
import { useAuth } from '@/contexts/AuthContext'

/**
 * Floating AI assistant, mounted once by the dashboard layout and therefore present on every
 * authenticated page.
 *
 * Layout safety: the launcher is the only thing this renders outside the drawer, and it is
 * `position: fixed` at the bottom-inline-end corner with the theme's `fab` z-index, so it
 * never participates in page flow. The dashboard layout raises the scroll-to-top button above
 * it so the two fixed controls cannot overlap.
 *
 * No streaming: the API exposes a single request/response `POST /ai/agent/chat`, so a turn
 * renders as an optimistic user bubble plus a working indicator, and the assistant's text
 * appears in one piece. Token streaming needs the SSE endpoint from a later backend phase.
 *
 * Deletes are the one thing the agent cannot do on its own: such a turn comes back parked
 * behind a confirmation token, which this panel surfaces as a modal the user has to answer
 * before anything is removed.
 */

const SUGGESTIONS = [
  'What tasks are blocked right now?',
  'Create a new story in my project',
  'Show me what\'s happening in project Alpha'
]

// Only re-pin to the newest message when the reader is already near the bottom; yanking the
// viewport while somebody is reading earlier turns is worse than a missed scroll.
const STICK_THRESHOLD_PX = 120

// Tool names are the only hint the panel gets about what a parked action would do, and they
// decide the wording of the confirmation ("Yes, delete" vs a neutral "Yes, go ahead").
const DESTRUCTIVE_TOOL = /delete|remove|archive/i

const AiChatPanel = () => {
  const { session } = useAuth()

  const {
    open,
    toggle,
    close,
    everOpened,
    sessionId,
    sessions,
    sessionsQuery,
    selectSession,
    startNewSession,
    deleteSession,
    isDeletingSession,
    transcript,
    messagesQuery,
    turnFor,
    send,
    retry,
    isSending,
    error,
    clearError,
    pendingConfirmation,
    confirmPending,
    dismissConfirmation,
    isConfirming,
    projectId
  } = useAiAgent()

  const scrollRef = useRef(null)
  const bottomAnchorRef = useRef(null)
  const stickToBottomRef = useRef(true)

  // The drawer is full-width on phones, where a 240px rail would leave no room to read. The
  // sidebar therefore follows the breakpoint until the user overrides it, and on narrow
  // screens it takes over the panel instead of sitting beside the transcript.
  const isWide = useMediaQuery(theme => theme.breakpoints.up('sm'))
  const [sidebarOverride, setSidebarOverride] = useState(null)
  const showSidebar = sidebarOverride ?? isWide

  const scrollToBottom = useCallback(behavior => {
    if (bottomAnchorRef.current) {
      bottomAnchorRef.current.scrollIntoView({ behavior: behavior || 'smooth', block: 'end' })
    } else if (scrollRef.current) {
      const element = scrollRef.current

      element.scrollTo({ top: element.scrollHeight, behavior: behavior || 'smooth' })
    }
  }, [])

  useEffect(() => {
    if (open && (stickToBottomRef.current || isSending)) {
      scrollToBottom('smooth')
    }
  }, [open, transcript.length, isSending, scrollToBottom])

  // Opening a conversation should always land on the newest message, without animation.
  useEffect(() => {
    stickToBottomRef.current = true

    if (open) scrollToBottom('auto')
  }, [sessionId, open, scrollToBottom])

  const handleScroll = event => {
    const element = event.currentTarget

    stickToBottomRef.current = element.scrollHeight - element.scrollTop - element.clientHeight < STICK_THRESHOLD_PX
  }

  const handleSend = text => {
    stickToBottomRef.current = true
    send(text)
    setTimeout(() => scrollToBottom('smooth'), 50)
  }

  // Signed-out shells (login, forbidden) never render the dashboard layout, but the guard
  // keeps the panel honest if it is ever mounted somewhere else.
  if (!session) return null

  const isLoadingTranscript = Boolean(sessionId) && messagesQuery.isLoading

  // Covers both a brand new conversation and an existing one that was created but never used.
  const showEmptyState = !transcript.length && !isSending && !isLoadingTranscript

  const pendingActions = pendingConfirmation?.pendingActions ?? []

  // Only deletes need confirming today, but the token is a generic "the agent wants
  // permission" channel, so the copy softens to a neutral wording for anything else rather
  // than promising a delete that is not happening.
  const isDeletionPending =
    pendingActions.length > 0 && pendingActions.every(action => DESTRUCTIVE_TOOL.test(action?.toolName ?? ''))

  // `pendingActions` is nullable on the wire. An empty list still needs a line, because a
  // dialog that asks "are you sure?" about nothing at all is worse than an honest placeholder.
  const pendingLines = pendingActions.length
    ? pendingActions.map(describePendingAction)
    : ['An action Anna did not describe']

  return (
    <>
      <Zoom in={!open} unmountOnExit>
        <Tooltip title='Ask Anna' placement='left'>
          <Fab
            color='primary'
            onClick={toggle}
            aria-label='Open Anna'
            className='mui-fixed'
            sx={{
              position: 'fixed',
              insetBlockEnd: 16,
              insetInlineEnd: 16,
              zIndex: 'var(--mui-zIndex-fab)'
            }}
          >
            <i className='tabler-sparkles' />
          </Fab>
        </Tooltip>
      </Zoom>

      <Drawer
        anchor='right'
        open={open}
        onClose={close}

        // `keepMounted` used to be unconditional, which made MUI render the whole drawer -
        // sidebar, transcript, composer, reindex button - into a hidden portal on every
        // dashboard page even for users who never open the assistant. Enabling it only after
        // the first open keeps the reopen-fast behaviour without paying for it on page load.
        keepMounted={everOpened}
        TransitionComponent={Slide}
        SlideProps={{ direction: 'left' }}
        transitionDuration={300}
        slotProps={{
          paper: {
            sx: {
              inlineSize: { xs: '100%', sm: 560, md: 640, lg: 720 },
              maxInlineSize: '100vw',
              display: 'flex',
              flexDirection: 'column'
            }
          }
        }}
      >
        <Box
          sx={{
            display: 'flex',
            alignItems: 'center',
            gap: 2,
            p: 4,
            borderBlockEnd: '1px solid',
            borderColor: 'divider'
          }}
        >
          <Box sx={{ flex: 1, minInlineSize: 0 }}>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 2 }}>
              <Typography variant='h6'>Anna</Typography>
              {projectId ? (
                <Tooltip title='Answers are scoped to the project you are viewing'>
                  <span>
                    <CustomChip size='small' variant='tonal' color='info' label={`Project #${projectId}`} />
                  </span>
                </Tooltip>
              ) : null}
            </Box>
            <Typography variant='caption' color='text.secondary'>
              Ask Anna questions, or ask her to create, update and delete work items. She always confirms before
              deleting.
            </Typography>
          </Box>

          <AiReindexButton />

          <Tooltip title={showSidebar ? 'Hide conversations' : 'Show conversations'}>
            <IconButton onClick={() => setSidebarOverride(!showSidebar)} aria-label='Toggle the conversation list'>
              <i className='tabler-history' />
            </IconButton>
          </Tooltip>
          <Tooltip title='New chat'>
            <IconButton onClick={startNewSession} aria-label='Start a new chat'>
              <i className='tabler-plus' />
            </IconButton>
          </Tooltip>
          <Tooltip title='Close'>
            <IconButton onClick={close} aria-label='Close Anna'>
              <i className='tabler-x' />
            </IconButton>
          </Tooltip>
        </Box>

        <Box sx={{ display: 'flex', flex: 1, minBlockSize: 0 }}>
          {showSidebar ? (
            <AiChatSidebar
              fullWidth={!isWide}
              sessions={sessions}
              sessionsQuery={sessionsQuery}
              activeSessionId={sessionId}
              onSelect={id => {
                selectSession(id)
                if (!isWide) setSidebarOverride(false)
              }}
              onNewSession={() => {
                startNewSession()
                if (!isWide) setSidebarOverride(false)
              }}
              onDelete={deleteSession}
              isDeleting={isDeletingSession}
            />
          ) : null}

          <Box
            sx={{
              flex: 1,
              display: showSidebar && !isWide ? 'none' : 'flex',
              flexDirection: 'column',
              minInlineSize: 0
            }}
          >
            <Box
              ref={scrollRef}
              onScroll={handleScroll}
              sx={{ flex: 1, overflowY: 'auto', p: 4, display: 'flex', flexDirection: 'column', gap: 4 }}
            >
              {isLoadingTranscript ? (
                <Box sx={{ display: 'flex', justifyContent: 'center', p: 6 }}>
                  <CircularProgress size={24} />
                </Box>
              ) : messagesQuery.isError ? (
                <Alert
                  severity='error'
                  action={
                    <Button color='inherit' size='small' onClick={() => messagesQuery.refetch()}>
                      Retry
                    </Button>
                  }
                >
                  {errorMessage(messagesQuery.error)}
                </Alert>
              ) : showEmptyState ? (
                <Box sx={{ m: 'auto', textAlign: 'center', display: 'flex', flexDirection: 'column', gap: 4 }}>
                  <Box>
                    <Typography variant='h6'>How can Anna help?</Typography>
                    <Typography variant='body2' color='text.secondary'>
                      Ask her about your projects, or tell her what to do — she&apos;ll handle it.
                    </Typography>
                  </Box>
                  <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
                    {SUGGESTIONS.map(suggestion => (
                      <Button key={suggestion} variant='tonal' color='secondary' onClick={() => handleSend(suggestion)}>
                        {suggestion}
                      </Button>
                    ))}
                  </Box>
                </Box>
              ) : (
                transcript.map(message => (
                  <AiChatMessage key={message.id} message={message} turn={turnFor(message.id)} />
                ))
              )}

              {/*
                The turn is a single request/response, with live animated reasoning stages.
                The indicator displays elapsed stopwatch counter and step progression.
              */}
              <ThinkingIndicator isSending={isSending} onUpdate={() => scrollToBottom('smooth')} />
              <Box ref={bottomAnchorRef} sx={{ minHeight: 24, height: 24 }} />
            </Box>

            {error ? (
              <Alert
                severity='error'

                // Both buttons live in `action`: MUI drops the built-in close button as soon
                // as `action` is supplied, so an `onClose` here would never be reachable.
                action={
                  <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                    {error.text ? (
                      <Button color='inherit' size='small' onClick={retry} disabled={isSending}>
                        Retry
                      </Button>
                    ) : null}
                    <IconButton size='small' color='inherit' onClick={clearError} aria-label='Dismiss error'>
                      <i className='tabler-x' style={{ fontSize: '1rem' }} />
                    </IconButton>
                  </Box>
                }
                sx={{ mx: 4, mb: 2 }}
              >
                {error.message}
              </Alert>
            ) : null}

            <AiChatComposer onSend={handleSend} sending={isSending} />
          </Box>
        </Box>
      </Drawer>

      {/*
        Rendered as a sibling of the drawer so it survives `keepMounted={false}`, and stacked
        above it by MUI's modal manager. It follows the panel's own visibility: a modal
        ambushing somebody who has closed the assistant would be worse than waiting, and
        waiting is free because nothing is deleted until `approve` is sent.
      */}
      <Dialog
        open={open && Boolean(pendingConfirmation)}
        onClose={() => {
          if (!isConfirming) dismissConfirmation()
        }}
        fullWidth
        maxWidth='xs'
        aria-labelledby='ai-confirm-title'
        aria-describedby='ai-confirm-description'
      >
        <DialogTitle id='ai-confirm-title'>
          {isDeletionPending ? 'Confirm before deleting' : 'Confirm with Anna'}
        </DialogTitle>
        <DialogContent>
          <Typography id='ai-confirm-description' color='text.secondary'>
            {isDeletionPending
              ? 'Anna will not delete anything until you say so. She is asking to:'
              : 'Anna is asking before she goes ahead with:'}
          </Typography>

          <Box
            component='ul'
            sx={{ listStyle: 'none', m: 0, mt: 3, p: 0, display: 'flex', flexDirection: 'column', gap: 2 }}
          >
            {pendingLines.map((line, index) => (
              <Box component='li' key={`${line}-${index}`} sx={{ display: 'flex', alignItems: 'flex-start', gap: 2 }}>
                <Box
                  component='i'
                  className={isDeletionPending ? 'tabler-trash' : 'tabler-player-play'}
                  sx={{ fontSize: '1rem', color: isDeletionPending ? 'error.main' : 'text.secondary', mt: 1 }}
                />
                <Typography variant='body2' sx={{ overflowWrap: 'anywhere' }}>
                  {line}
                </Typography>
              </Box>
            ))}
          </Box>

          {isDeletionPending ? (
            <Typography variant='caption' color='text.secondary' sx={{ display: 'block', mt: 3 }}>
              This cannot be undone.
            </Typography>
          ) : null}

          {/* The dialog covers the panel's error banner, so a failed decision reports here. */}
          {error ? (
            <Alert severity='error' sx={{ mt: 3 }}>
              {error.message}
            </Alert>
          ) : null}
        </DialogContent>
        <DialogActions>
          <Button onClick={() => confirmPending(AI_CONFIRM_ACTION.cancel)} disabled={isConfirming}>
            Cancel
          </Button>
          <Button
            color='error'
            variant='contained'
            onClick={() => confirmPending(AI_CONFIRM_ACTION.approve)}
            disabled={isConfirming}
            startIcon={isConfirming ? <CircularProgress size={16} color='inherit' /> : null}
          >
            {isDeletionPending ? 'Yes, delete' : 'Yes, go ahead'}
          </Button>
        </DialogActions>
      </Dialog>
    </>
  )
}

export default AiChatPanel
