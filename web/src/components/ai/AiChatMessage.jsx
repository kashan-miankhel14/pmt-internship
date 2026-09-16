'use client'

import { useState, useEffect, useRef } from 'react'
import { useRouter } from 'next/navigation'

import Avatar from '@mui/material/Avatar'
import Box from '@mui/material/Box'
import Paper from '@mui/material/Paper'
import Tooltip from '@mui/material/Tooltip'
import Typography from '@mui/material/Typography'
import Slide from '@mui/material/Slide'
import Chip from '@mui/material/Chip'

import AiMarkdown from '@/components/ai/AiMarkdown'
import AiToolExecution from '@/components/ai/AiToolExecution'
import { AI_ROLE, formatChatTime, formatDuration, PENDING_MESSAGE_ID } from '@/libs/aiChat'

const CITATION_ROUTES = { Project: id => `/projects/${id}` }

const CitationChip = ({ citation, onNavigate }) => {
  const href = CITATION_ROUTES[citation.entityType]?.(citation.entityId)
  const label = `${citation.entityType} #${citation.entityId}${citation.title ? ` - ${citation.title}` : ''}`

  return (
    <Tooltip title={citation.content?.slice(0, 300) || label}>
      <span>
        <Chip
          size='small'
          variant='tonal'
          color={href ? 'primary' : 'secondary'}
          label={label}
          onClick={href ? () => onNavigate(href) : undefined}
          sx={{ maxInlineSize: 260, cursor: href ? 'pointer' : 'default', fontSize: '0.72rem', height: 22 }}
        />
      </span>
    </Tooltip>
  )
}

/**
 * Typewriter progressive stream component for fresh assistant responses
 */
const TypewriterText = ({ fullText, isFresh }) => {
  const [displayedLength, setDisplayedLength] = useState(isFresh ? 0 : fullText.length)
  const isComplete = displayedLength >= fullText.length

  useEffect(() => {
    if (!isFresh || isComplete) {
      setDisplayedLength(fullText.length)
      return
    }

    // Smooth ChatGPT/Gemini style progressive text streaming
    const totalChars = fullText.length
    const stepSize = Math.max(3, Math.ceil(totalChars / 35)) // Complete in ~30-40 frames (around 400-800ms)
    
    const timer = setInterval(() => {
      setDisplayedLength(prev => {
        const next = prev + stepSize
        if (next >= totalChars) {
          clearInterval(timer)
          return totalChars
        }
        return next
      })
    }, 18)

    return () => clearInterval(timer)
  }, [fullText, isFresh, isComplete])

  const textToRender = fullText.slice(0, displayedLength)

  return (
    <Box onClick={() => setDisplayedLength(fullText.length)} sx={{ cursor: isComplete ? 'default' : 'pointer' }}>
      <AiMarkdown content={textToRender} />
      {!isComplete && (
        <Box
          component='span'
          sx={{
            display: 'inline-block',
            width: '6px',
            height: '14px',
            bgcolor: 'primary.main',
            ml: 0.5,
            verticalAlign: 'middle',
            animation: 'blink 0.7s infinite',
            '@keyframes blink': {
              '0%, 100%': { opacity: 1 },
              '50%': { opacity: 0 }
            }
          }}
        />
      )}
    </Box>
  )
}

const AiChatMessage = ({ message, turn }) => {
  const router = useRouter()
  const isUser = message.role === AI_ROLE.user
  const isSystem = message.role === AI_ROLE.system
  const citations = message.citations ?? []

  // Check if message is brand new (created in the last 15 seconds) to trigger progressive streaming
  const isFreshAssistantTurn = useRef(
    !isUser && !isSystem && message.id !== PENDING_MESSAGE_ID &&
    (Date.now() - new Date(message.createdAt || Date.now()).getTime()) < 15000
  ).current

  const showToolCalls = !isUser && !isSystem && Boolean(turn?.toolCalls?.length || turn?.truncated)

  if (isSystem) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', px: 4, my: 1 }}>
        <Typography variant='caption' color='text.secondary' sx={{ textAlign: 'center', bgcolor: 'action.hover', px: 2, py: 0.5, borderRadius: 1 }}>
          {message.content}
        </Typography>
      </Box>
    )
  }

  return (
    <Slide direction='up' in timeout={300}>
      <Box sx={{ display: 'flex', gap: 2, flexDirection: isUser ? 'row-reverse' : 'row', alignItems: 'flex-start', my: 1 }}>
        <Avatar
          sx={{
            width: 32,
            height: 32,
            bgcolor: isUser ? 'primary.main' : '#6366f1',
            color: 'common.white',
            boxShadow: isUser ? 'none' : '0 2px 8px rgba(99, 102, 241, 0.3)',
            flexShrink: 0
          }}
        >
          <i className={isUser ? 'tabler-user' : 'tabler-sparkles'} style={{ fontSize: '1.1rem' }} />
        </Avatar>

        <Box
          sx={{
            display: 'flex',
            flexDirection: 'column',
            gap: 1,
            maxInlineSize: { xs: '90%', sm: '82%' },
            alignItems: isUser ? 'flex-end' : 'stretch'
          }}
        >
          {/* Tool execution telemetry badge (shown on top of response or underneath) */}
          {showToolCalls && !isUser ? (
            <AiToolExecution toolCalls={turn.toolCalls ?? []} steps={turn.steps} truncated={turn.truncated} />
          ) : null}

          <Paper
            elevation={0}
            sx={{
              px: 3.5,
              py: 2.5,
              borderRadius: 2,
              border: isUser ? 0 : 1,
              borderColor: 'divider',
              bgcolor: isUser ? 'primary.main' : 'background.paper',
              color: isUser ? 'primary.contrastText' : 'text.primary',
              opacity: message.id === PENDING_MESSAGE_ID ? 0.7 : 1,
              boxShadow: isUser ? '0 2px 10px rgba(99, 102, 241, 0.25)' : '0 1px 4px rgba(0,0,0,0.04)'
            }}
          >
            {isUser ? (
              <Typography variant='body2' color='inherit' sx={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere', lineHeight: 1.5 }}>
                {message.content}
              </Typography>
            ) : (
              <TypewriterText fullText={message.content} isFresh={isFreshAssistantTurn} />
            )}
          </Paper>

          {citations.length ? (
            <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1, mt: 0.5 }}>
              {citations.map(citation => (
                <CitationChip key={citation.id} citation={citation} onNavigate={href => router.push(href)} />
              ))}
            </Box>
          ) : null}

          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, px: 1, mt: 0.2 }}>
            <Typography variant='caption' color='text.disabled' sx={{ fontSize: '0.68rem' }}>
              {formatChatTime(message.createdAt)}
            </Typography>
            {message.latencyMs ? (
              <Typography variant='caption' color='text.disabled' sx={{ fontSize: '0.68rem', fontFamily: 'monospace' }}>
                ⚡ {formatDuration(message.latencyMs)}
              </Typography>
            ) : null}
            {message.tokenCount ? (
              <Typography variant='caption' color='text.disabled' sx={{ fontSize: '0.68rem', fontFamily: 'monospace' }}>
                · {message.tokenCount} tokens
              </Typography>
            ) : null}
          </Box>
        </Box>
      </Box>
    </Slide>
  )
}

export default AiChatMessage
