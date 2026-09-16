'use client'

import { useEffect, useState, useRef } from 'react'
import Box from '@mui/material/Box'
import Typography from '@mui/material/Typography'
import Chip from '@mui/material/Chip'
import Collapse from '@mui/material/Collapse'
import IconButton from '@mui/material/IconButton'

// Live contextual thinking & telemetry stages like Gemini / ChatGPT Thinking models
const TELEMETRY_STAGES = [
  { id: 'intent', after: 0, text: 'Analyzing context & user intent…', icon: 'tabler-brain' },
  { id: 'db', after: 2, text: 'Querying project repository & database…', icon: 'tabler-database-search' },
  { id: 'entities', after: 6, text: 'Evaluating stories, sprints & task states…', icon: 'tabler-hierarchy' },
  { id: 'tools', after: 11, text: 'Executing assistant tools & mutations…', icon: 'tabler-cpu' },
  { id: 'synthesis', after: 18, text: 'Synthesizing response & live telemetry…', icon: 'tabler-sparkles' },
  { id: 'reasoning', after: 28, text: 'Deep reasoning & formatting output…', icon: 'tabler-wand' },
  { id: 'polishing', after: 42, text: 'Finalizing response stream…', icon: 'tabler-clock-hour-4' },
]

const ThinkingIndicator = ({ isSending, onUpdate }) => {
  const [elapsedSeconds, setElapsedSeconds] = useState(0)
  const [expanded, setExpanded] = useState(true)
  const containerRef = useRef(null)

  useEffect(() => {
    if (!isSending) {
      setElapsedSeconds(0)
      return
    }

    const start = Date.now()
    const interval = setInterval(() => {
      setElapsedSeconds(Math.floor((Date.now() - start) / 1000))
    }, 500)

    return () => clearInterval(interval)
  }, [isSending])

  // Scroll into view whenever thinking indicator updates
  useEffect(() => {
    if (isSending && containerRef.current) {
      containerRef.current.scrollIntoView({ behavior: 'smooth', block: 'end' })
      if (onUpdate) onUpdate()
    }
  }, [isSending, elapsedSeconds, onUpdate])

  if (!isSending) return null

  const currentStageIndex = TELEMETRY_STAGES.findIndex((s, idx) => {
    const next = TELEMETRY_STAGES[idx + 1]
    return elapsedSeconds >= s.after && (!next || elapsedSeconds < next.after)
  })

  const currentStage = TELEMETRY_STAGES[currentStageIndex !== -1 ? currentStageIndex : 0]

  return (
    <Box
      ref={containerRef}
      role='status'
      aria-live='polite'
      sx={{
        display: 'flex',
        flexDirection: 'column',
        gap: 1.5,
        p: 3,
        my: 1.5,
        mb: 3,
        borderRadius: 2.5,
        border: '1px solid',
        borderColor: 'primary.lightOpacity',
        background: 'linear-gradient(135deg, rgba(99, 102, 241, 0.06) 0%, rgba(168, 85, 247, 0.06) 100%)',
        boxShadow: '0 4px 20px rgba(99, 102, 241, 0.08)',
        backdropFilter: 'blur(10px)',
        position: 'relative',
        overflow: 'hidden'
      }}
    >
      {/* Animated top shimmer bar */}
      <Box
        sx={{
          position: 'absolute',
          top: 0,
          left: 0,
          right: 0,
          height: 2.5,
          background: 'linear-gradient(90deg, #6366f1, #a855f7, #ec4899, #6366f1)',
          backgroundSize: '200% 100%',
          animation: 'shimmer 2s linear infinite',
          '@keyframes shimmer': {
            '0%': { backgroundPosition: '0% 0%' },
            '100%': { backgroundPosition: '200% 0%' }
          }
        }}
      />

      {/* Header Bar */}
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 2 }}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
          {/* Pulsing AI Orb */}
          <Box
            sx={{
              width: 26,
              height: 26,
              borderRadius: '50%',
              background: 'linear-gradient(135deg, #6366f1 0%, #a855f7 100%)',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              color: 'white',
              boxShadow: '0 0 14px rgba(99, 102, 241, 0.45)',
              animation: 'pulseGlow 1.8s ease-in-out infinite',
              '@keyframes pulseGlow': {
                '0%, 100%': { transform: 'scale(0.95)', boxShadow: '0 0 6px rgba(99, 102, 241, 0.3)' },
                '50%': { transform: 'scale(1.08)', boxShadow: '0 0 18px rgba(168, 85, 247, 0.65)' }
              }
            }}
          >
            <i className={`${currentStage.icon} text-xs`} />
          </Box>

          <Box sx={{ display: 'flex', flexDirection: 'column' }}>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
              <Typography
                variant='caption'
                sx={{
                  fontWeight: 800,
                  color: 'primary.main',
                  textTransform: 'uppercase',
                  letterSpacing: '0.08em',
                  fontSize: '0.68rem'
                }}
              >
                Anna Thinking &amp; Reasoning
              </Typography>
              <Box
                sx={{
                  width: 6,
                  height: 6,
                  borderRadius: '50%',
                  bgcolor: 'success.main',
                  animation: 'blink 1s ease-in-out infinite',
                  '@keyframes blink': {
                    '0%, 100%': { opacity: 1 },
                    '50%': { opacity: 0.2 }
                  }
                }}
              />
            </Box>
            <Typography variant='body2' sx={{ color: 'text.primary', fontWeight: 600, fontSize: '0.84rem' }}>
              {currentStage.text}
            </Typography>
          </Box>
        </Box>

        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          {/* Stopwatch badge */}
          <Chip
            size='small'
            label={`${elapsedSeconds}s`}
            icon={<i className='tabler-clock text-xs' />}
            sx={{
              fontFamily: 'monospace',
              fontWeight: 700,
              fontSize: '0.72rem',
              height: 22,
              bgcolor: 'action.hover',
              border: '1px solid',
              borderColor: 'divider'
            }}
          />
          <IconButton size='small' onClick={() => setExpanded(!expanded)} aria-label='Toggle thinking steps'>
            <i className={expanded ? 'tabler-chevron-up text-xs' : 'tabler-chevron-down text-xs'} />
          </IconButton>
        </Box>
      </Box>

      {/* Expandable Live Progression Steps */}
      <Collapse in={expanded}>
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1, pt: 1, pl: 1, borderTop: '1px solid', borderColor: 'divider' }}>
          {TELEMETRY_STAGES.slice(0, Math.min(TELEMETRY_STAGES.length, (currentStageIndex !== -1 ? currentStageIndex : 0) + 1)).map((stage, idx) => {
            const isCompleted = idx < currentStageIndex
            const isCurrent = idx === currentStageIndex

            return (
              <Box key={stage.id} sx={{ display: 'flex', alignItems: 'center', gap: 1.5, fontSize: '0.78rem' }}>
                {isCompleted ? (
                  <i className='tabler-check text-success text-xs font-bold' />
                ) : (
                  <Box
                    sx={{
                      width: 8,
                      height: 8,
                      borderRadius: '50%',
                      bgcolor: 'primary.main',
                      animation: 'pulse 1.2s infinite'
                    }}
                  />
                )}
                <Typography
                  variant='caption'
                  sx={{
                    fontWeight: isCurrent ? 700 : 500,
                    color: isCurrent ? 'text.primary' : 'text.secondary',
                    fontFamily: isCurrent ? 'inherit' : 'monospace',
                    fontSize: '0.75rem'
                  }}
                >
                  {stage.text}
                </Typography>
              </Box>
            )
          })}
        </Box>
      </Collapse>
    </Box>
  )
}

export default ThinkingIndicator
