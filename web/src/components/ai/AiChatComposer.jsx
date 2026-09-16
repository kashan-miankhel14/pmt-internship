'use client'

import { useState } from 'react'

import Box from '@mui/material/Box'
import CircularProgress from '@mui/material/CircularProgress'
import IconButton from '@mui/material/IconButton'
import Typography from '@mui/material/Typography'

import CustomTextField from '@core/components/mui/TextField'

import { AI_MAX_MESSAGE_LENGTH } from '@/services/aiAgent'

/**
 * Message input for the agent.
 *
 * Free text only, deliberately: there is no command syntax to learn, so the placeholder
 * carries the whole affordance by showing that plain requests ("Create a story in Alpha")
 * are as valid as questions.
 *
 * The draft lives here rather than in the hook: it is pure keystroke state and lifting it
 * would re-render the whole transcript on every character. `onSend` owns the request, so the
 * composer clears optimistically and a failed turn is recovered through the panel's retry
 * banner instead of by restoring the text (which would risk sending the same turn twice).
 */

// Warn only once the limit is close enough to matter; a counter on every keystroke is noise.
const COUNTER_THRESHOLD = 0.8 * AI_MAX_MESSAGE_LENGTH

const AiChatComposer = ({ onSend, disabled = false, sending = false }) => {
  const [draft, setDraft] = useState('')

  const trimmed = draft.trim()
  const canSend = Boolean(trimmed) && !disabled && !sending

  const submit = () => {
    if (!canSend) return

    onSend(trimmed)
    setDraft('')
  }

  const handleKeyDown = event => {
    // Enter sends, Shift+Enter inserts a newline. IME composition must never submit early.
    if (event.key === 'Enter' && !event.shiftKey && !event.nativeEvent.isComposing) {
      event.preventDefault()
      submit()
    }
  }

  return (
    <Box
      sx={{
        display: 'flex',
        flexDirection: 'column',
        gap: 1,
        p: 4,

        // MUI's border system only maps the physical props, so logical borders must be
        // written out in full or they resolve to `border-style: none` and vanish.
        borderBlockStart: '1px solid',
        borderColor: 'divider'
      }}
    >
      <Box sx={{ display: 'flex', alignItems: 'flex-end', gap: 2 }}>
        <CustomTextField
          fullWidth
          multiline
          minRows={1}
          maxRows={6}
          value={draft}
          onChange={event => setDraft(event.target.value.slice(0, AI_MAX_MESSAGE_LENGTH))}
          onKeyDown={handleKeyDown}
          disabled={disabled}
          placeholder={
            sending ? 'Waiting for Anna...' : 'Ask Anna... e.g. "Create a story in Alpha" or "Delete task #12"'
          }
          slotProps={{ htmlInput: { maxLength: AI_MAX_MESSAGE_LENGTH, 'aria-label': 'Message Anna' } }}
        />
        <IconButton color='primary' onClick={submit} disabled={!canSend} aria-label='Send message' sx={{ mb: 1 }}>
          {sending ? <CircularProgress size={20} /> : <i className='tabler-send' />}
        </IconButton>
      </Box>
      <Box sx={{ display: 'flex', justifyContent: 'space-between', gap: 2 }}>
        <Typography variant='caption' color='text.disabled'>
          Enter to send, Shift+Enter for a new line
        </Typography>
        {draft.length >= COUNTER_THRESHOLD ? (
          <Typography variant='caption' color={draft.length >= AI_MAX_MESSAGE_LENGTH ? 'error.main' : 'text.disabled'}>
            {draft.length} / {AI_MAX_MESSAGE_LENGTH}
          </Typography>
        ) : null}
      </Box>
    </Box>
  )
}

export default AiChatComposer
