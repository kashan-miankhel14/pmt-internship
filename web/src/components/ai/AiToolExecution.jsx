'use client'

import { useState } from 'react'

import Box from '@mui/material/Box'
import ButtonBase from '@mui/material/ButtonBase'
import Collapse from '@mui/material/Collapse'
import Typography from '@mui/material/Typography'
import Chip from '@mui/material/Chip'

import { AI_TOOL_STATUS, formatDuration, formatJson } from '@/libs/aiChat'

const STATUS_META = {
  [AI_TOOL_STATUS.completed]: { icon: 'tabler-check', color: '#10b981', bg: 'rgba(16, 185, 129, 0.1)', label: 'Completed' },
  [AI_TOOL_STATUS.failed]: { icon: 'tabler-alert-triangle', color: '#ef4444', bg: 'rgba(239, 68, 68, 0.1)', label: 'Failed' },
  [AI_TOOL_STATUS.running]: { icon: 'tabler-loader-2', color: '#6366f1', bg: 'rgba(99, 102, 241, 0.1)', label: 'Running' },
  [AI_TOOL_STATUS.pending]: { icon: 'tabler-clock', color: '#64748b', bg: 'rgba(100, 116, 139, 0.1)', label: 'Pending' }
}

const ToolCallRow = ({ toolCall }) => {
  const [open, setOpen] = useState(false)
  const meta = STATUS_META[toolCall.status] ?? STATUS_META[AI_TOOL_STATUS.pending]
  const hasDetail = Boolean(toolCall.argumentsJson || toolCall.resultJson)

  return (
    <Box
      sx={{
        borderRadius: 1.5,
        border: '1px solid',
        borderColor: 'divider',
        bgcolor: 'background.paper',
        overflow: 'hidden',
        transition: 'all 0.2s ease',
        '&:hover': { borderColor: 'primary.main', boxShadow: '0 2px 8px rgba(0,0,0,0.04)' }
      }}
    >
      <ButtonBase
        onClick={() => hasDetail && setOpen(current => !current)}
        disabled={!hasDetail}
        sx={{
          inlineSize: '100%',
          justifyContent: 'flex-start',
          gap: 1.5,
          px: 3,
          py: 2,
          textAlign: 'start'
        }}
        aria-expanded={open}
        aria-label={`${toolCall.toolName} details`}
      >
        <Box
          sx={{
            width: 22,
            height: 22,
            borderRadius: '6px',
            bgcolor: meta.bg,
            color: meta.color,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            fontSize: '0.8rem'
          }}
        >
          <i className={meta.icon} />
        </Box>

        <Box sx={{ flex: 1, minWidth: 0 }}>
          <Typography variant='body2' sx={{ fontFamily: 'monospace', fontWeight: 600, fontSize: '0.78rem', color: 'text.primary' }}>
            {toolCall.toolName}
          </Typography>
        </Box>

        {toolCall.durationMs !== null && toolCall.durationMs !== undefined ? (
          <Chip
            size='small'
            label={formatDuration(toolCall.durationMs)}
            sx={{
              height: 20,
              fontSize: '0.68rem',
              fontFamily: 'monospace',
              fontWeight: 600,
              bgcolor: 'action.hover'
            }}
          />
        ) : null}

        {hasDetail ? (
          <Box
            component='i'
            className={open ? 'tabler-chevron-up' : 'tabler-chevron-down'}
            sx={{ fontSize: '1rem', color: 'text.secondary', transition: 'transform 0.2s' }}
          />
        ) : null}
      </ButtonBase>

      <Collapse in={open} unmountOnExit>
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2, px: 3, pb: 3, pt: 1, borderTop: '1px solid', borderColor: 'divider', bgcolor: 'action.hover' }}>
          {toolCall.argumentsJson ? (
            <Box>
              <Typography variant='caption' sx={{ fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'text.secondary', fontSize: '0.65rem' }}>
                Tool Arguments (Input)
              </Typography>
              <Box
                component='pre'
                sx={{
                  m: 0,
                  mt: 0.5,
                  p: 2,
                  borderRadius: 1,
                  bgcolor: 'background.paper',
                  border: '1px solid',
                  borderColor: 'divider',
                  overflowX: 'auto',
                  fontFamily: 'monospace',
                  fontSize: '0.72rem',
                  color: 'text.primary',
                  lineHeight: 1.4
                }}
              >
                {formatJson(toolCall.argumentsJson)}
              </Box>
            </Box>
          ) : null}
          {toolCall.resultJson ? (
            <Box>
              <Typography variant='caption' sx={{ fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'text.secondary', fontSize: '0.65rem' }}>
                Tool Output (Observation)
              </Typography>
              <Box
                component='pre'
                sx={{
                  m: 0,
                  mt: 0.5,
                  p: 2,
                  borderRadius: 1,
                  bgcolor: 'background.paper',
                  border: '1px solid',
                  borderColor: 'divider',
                  overflowX: 'auto',
                  fontFamily: 'monospace',
                  fontSize: '0.72rem',
                  color: 'text.primary',
                  lineHeight: 1.4
                }}
              >
                {formatJson(toolCall.resultJson)}
              </Box>
            </Box>
          ) : null}
        </Box>
      </Collapse>
    </Box>
  )
}

const IncompleteChip = () => (
  <Chip size='small' color='warning' variant='tonal' label='Answer may be incomplete' sx={{ fontSize: '0.7rem', height: 22 }} />
)

const AiToolExecution = ({ toolCalls = [], running = false, truncated = false, steps }) => {
  const [open, setOpen] = useState(false)

  if (!toolCalls.length) return truncated ? <IncompleteChip /> : null

  const failed = toolCalls.filter(call => call.status === AI_TOOL_STATUS.failed).length

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.5, my: 0.5 }}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, flexWrap: 'wrap' }}>
        <Chip
          size='small'
          variant='tonal'
          color={failed ? 'error' : 'primary'}
          icon={<i className={open ? 'tabler-cpu-2' : 'tabler-cpu'} />}
          label={`${toolCalls.length} tool ${toolCalls.length === 1 ? 'action' : 'actions'} executed${failed ? ` (${failed} failed)` : ''}`}
          onClick={() => setOpen(current => !current)}
          sx={{
            cursor: 'pointer',
            fontWeight: 600,
            fontSize: '0.74rem',
            height: 24,
            transition: 'all 0.2s',
            '&:hover': { transform: 'scale(1.02)' }
          }}
        />

        {steps ? (
          <Typography variant='caption' color='text.secondary' sx={{ fontSize: '0.72rem', fontFamily: 'monospace' }}>
            ({steps} reasoning {steps === 1 ? 'step' : 'steps'})
          </Typography>
        ) : null}

        {truncated ? <IncompleteChip /> : null}
      </Box>

      <Collapse in={open} unmountOnExit>
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.5, pt: 0.5 }}>
          {toolCalls.map(toolCall => (
            <ToolCallRow key={toolCall.id ?? `${toolCall.toolName}-${toolCall.startedAt}`} toolCall={toolCall} />
          ))}
        </Box>
      </Collapse>
    </Box>
  )
}

export default AiToolExecution
