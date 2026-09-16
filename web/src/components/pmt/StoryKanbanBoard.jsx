'use client'

// React Imports
import { memo, useEffect, useMemo, useRef, useState } from 'react'

// MUI Imports
import Typography from '@mui/material/Typography'
import Avatar from '@mui/material/Avatar'
import Tooltip from '@mui/material/Tooltip'

// Third-party Imports
import { useDragAndDrop } from '@formkit/drag-and-drop/react'

// Component Imports
import StatusChip from '@/components/pmt/StatusChip'

// Lib Imports
import { priorityLabel } from '@/libs/enums'

const FALLBACK_STATUSES = ['Backlog', 'Ready', 'InProgress', 'Review', 'Done']

const FALLBACK_COLUMNS = FALLBACK_STATUSES.map((status, index) => ({
  name: status,
  status,
  ordinal: index,
  completeColumn: status === 'Done'
}))

const EMPTY_STORIES = []
const EMPTY_INDEX = new Map()

const humanize = value => String(value || '').replace(/([a-z])([A-Z])/g, '$1 $2').toUpperCase()

const columnColorMap = {
  Backlog: { bar: '#64748b', bg: 'rgba(100, 116, 139, 0.05)', border: 'rgba(100, 116, 139, 0.2)' },
  Ready: { bar: '#0284c7', bg: 'rgba(2, 132, 199, 0.05)', border: 'rgba(2, 132, 199, 0.2)' },
  InProgress: { bar: '#3b82f6', bg: 'rgba(59, 130, 246, 0.05)', border: 'rgba(59, 130, 246, 0.2)' },
  Review: { bar: '#a855f7', bg: 'rgba(168, 85, 247, 0.05)', border: 'rgba(168, 85, 247, 0.2)' },
  Done: { bar: '#22c55e', bg: 'rgba(34, 197, 94, 0.05)', border: 'rgba(34, 197, 94, 0.2)' }
}

const indexById = items => {
  if (!Array.isArray(items) || !items.length) return EMPTY_INDEX
  const index = new Map()

  for (const item of items) index.set(item.id, item)
  
return index
}

const getInitials = name => {
  if (!name) return '?'
  
return name
    .split(' ')
    .map(n => n[0])
    .join('')
    .substring(0, 2)
    .toUpperCase()
}

// Story Card
const StoryCard = memo(({ story, lookupIndex, onStoryClick }) => {
  const assignee = lookupIndex.users.get(story.assignedToUserId || story.assigneeUserId)
  const project = lookupIndex.projects.get(story.projectId)
  const team = lookupIndex.teams?.get(story.teamId)

  return (
    <div
      className='cursor-grab select-none rounded-lg border border-[var(--mui-palette-divider)] bg-backgroundPaper p-2.5 shadow-xs transition-all duration-200 ease-out hover:border-[var(--mui-palette-primary-main)] hover:shadow-sm active:cursor-grabbing'
      onClick={() => onStoryClick?.(story)}
    >
      {/* Top row: Key & Priority */}
      <div className='flex items-center justify-between gap-1.5 mb-1.5'>
        <span className='font-mono text-[10px] font-bold px-1.5 py-0.5 rounded bg-primary-lighter/20 text-primary-main border border-primary-light/30'>
          STORY-{story.id}
        </span>
        <StatusChip value={priorityLabel(story.priority)} size='small' />
      </div>

      {/* Title */}
      <Typography variant='subtitle2' className='font-semibold text-textPrimary leading-snug mb-1 line-clamp-2 text-xs'>
        {story.title}
      </Typography>

      {/* Description */}
      {story.description && (
        <Typography variant='body2' color='text.secondary' className='text-[11px] line-clamp-1 mb-1.5'>
          {story.description}
        </Typography>
      )}

      {/* Tags: Project, Team & Story Points */}
      <div className='flex flex-wrap items-center gap-1 mb-1.5'>
        {project && (
          <span className='rounded bg-actionHover px-1.5 py-0.5 text-[10px] font-medium text-textSecondary'>
            {project.key || project.name}
          </span>
        )}
        {team && (
          <span className='rounded bg-secondary-lighter/20 text-secondary-dark px-1.5 py-0.5 text-[10px] font-medium'>
            👥 {team.name}
          </span>
        )}
        {story.storyPoints != null && (
          <span className='rounded bg-purple-500/10 text-purple-600 border border-purple-500/20 px-1.5 py-0.5 text-[10px] font-bold'>
            {story.storyPoints} pts
          </span>
        )}
      </div>

      {/* Bottom row: Assignee */}
      <div className='flex items-center justify-between gap-1.5 pt-1.5 border-t border-[var(--mui-palette-divider)] text-[11px] text-textSecondary'>
        <div className='flex items-center gap-1'>
          {assignee ? (
            <Tooltip title={`Assignee: ${assignee.displayName || assignee.email}`}>
              <Avatar sx={{ width: 18, height: 18, fontSize: '0.6rem', bgcolor: 'primary.main', fontWeight: 600 }}>
                {getInitials(assignee.displayName || assignee.email)}
              </Avatar>
            </Tooltip>
          ) : (
            <Tooltip title='Unassigned'>
              <Avatar sx={{ width: 18, height: 18, fontSize: '0.6rem', bgcolor: 'action.hover', color: 'text.disabled' }}>
                <i className='tabler-user text-[10px]' />
              </Avatar>
            </Tooltip>
          )}
          <span className='truncate max-w-[100px] text-[10px]'>
            {assignee ? (assignee.displayName || assignee.email).split(' ')[0] : 'Unassigned'}
          </span>
        </div>
      </div>
    </div>
  )
})

const StoryKanbanColumn = memo(({ status, label, stories, lookupIndex, onStoryMove, onStoryClick, isDraggingRef, pendingMoveRef }) => {
  const onStoryMoveRef = useRef(onStoryMove)

  onStoryMoveRef.current = onStoryMove

  const styleConfig = columnColorMap[status] || { bar: '#3b82f6', bg: 'rgba(59, 130, 246, 0.05)', border: 'rgba(59, 130, 246, 0.2)' }

  const [parent, values, setValues] = useDragAndDrop(stories, {
    group: 'pmt-story-kanban',
    sortable: true,
    onDragstart: () => {
      isDraggingRef.current = true
      pendingMoveRef.current = null
    },
    onDragend: () => {
      isDraggingRef.current = false
      const pending = pendingMoveRef.current

      pendingMoveRef.current = null
      if (pending) onStoryMoveRef.current(pending.story, pending.toStatus)
    },
    onTransfer: ({ initialParent, targetParent, draggedNodes }) => {
      const toStatus = targetParent?.el?.getAttribute('data-status')
      const fromStatus = initialParent?.el?.getAttribute('data-status')
      const story = draggedNodes?.[0]?.data?.value

      if (story && toStatus && fromStatus && toStatus === status && fromStatus !== status) {
        pendingMoveRef.current = { story, toStatus }
      }
    }
  })

  useEffect(() => {
    if (!isDraggingRef.current) setValues(stories)
  }, [stories, setValues, isDraggingRef])

  return (
    <div className='flex min-w-[240px] flex-1 flex-col rounded-lg border border-[var(--mui-palette-divider)] bg-actionHover/20 overflow-hidden shadow-2xs'>
      <div
        className='flex items-center justify-between px-3 py-2 border-b border-[var(--mui-palette-divider)] bg-backgroundPaper'
        style={{ borderTop: `2.5px solid ${styleConfig.bar}` }}
      >
        <Typography variant='subtitle2' className='font-bold text-textPrimary tracking-wide text-[11px] uppercase'>
          {humanize(label ?? status)}
        </Typography>
        <span
          className='flex items-center justify-center min-w-[18px] h-[18px] px-1 rounded-full text-[10px] font-bold'
          style={{ backgroundColor: styleConfig.bar, color: '#ffffff' }}
        >
          {values.length}
        </span>
      </div>
      <div
        ref={parent}
        data-status={status}
        className='flex flex-col gap-1.5 p-2 min-h-[220px] max-h-[calc(100vh-290px)] overflow-y-auto flex-1'
      >
        {values.length === 0 ? (
          <div className='flex flex-1 items-center justify-center rounded border border-dashed border-[var(--mui-palette-divider)] p-4 text-center text-[11px] text-textDisabled'>
            No stories in {humanize(label ?? status)}
          </div>
        ) : (
          values.map(story => (
            <StoryCard key={story.id} story={story} lookupIndex={lookupIndex} onStoryClick={onStoryClick} />
          ))
        )}
      </div>
    </div>
  )
})

const StoryKanbanBoard = ({ board, columns, lookups, onStoryMove, onStoryClick, isDraggingRef, pendingMoveRef }) => {
  const topScrollRef = useRef(null)
  const bottomScrollRef = useRef(null)
  const [scrollWidth, setScrollWidth] = useState(0)

  const lookupIndex = useMemo(
    () => ({
      users: indexById(lookups?.users),
      projects: indexById(lookups?.projects),
      teams: indexById(lookups?.teams)
    }),
    [lookups]
  )

  const activeColumns = columns?.length ? columns : FALLBACK_COLUMNS

  useEffect(() => {
    const updateWidth = () => {
      if (bottomScrollRef.current) {
        setScrollWidth(bottomScrollRef.current.scrollWidth)
      }
    }

    updateWidth()
    window.addEventListener('resize', updateWidth)
    
return () => window.removeEventListener('resize', updateWidth)
  }, [activeColumns, board])

  const handleTopScroll = e => {
    if (bottomScrollRef.current && Math.abs(bottomScrollRef.current.scrollLeft - e.target.scrollLeft) > 1) {
      bottomScrollRef.current.scrollLeft = e.target.scrollLeft
    }
  }

  const handleBottomScroll = e => {
    if (topScrollRef.current && Math.abs(topScrollRef.current.scrollLeft - e.target.scrollLeft) > 1) {
      topScrollRef.current.scrollLeft = e.target.scrollLeft
    }
  }

  return (
    <div className='flex flex-col gap-1 w-full'>
      {/* Top Scrollbar */}
      <div
        ref={topScrollRef}
        onScroll={handleTopScroll}
        className='w-full overflow-x-auto h-2.5 mb-2 top-scrollbar rounded bg-actionHover/30 border border-[var(--mui-palette-divider)]'
      >
        <div style={{ width: scrollWidth || '100%', height: 1 }} />
      </div>

      {/* Main Board - Bottom scrollbar hidden */}
      <div
        ref={bottomScrollRef}
        onScroll={handleBottomScroll}
        className='w-full overflow-x-auto no-scrollbar pb-2'
      >
        <div
          className='grid gap-2.5 items-start min-w-full'
          style={{ gridTemplateColumns: `repeat(${activeColumns.length}, minmax(240px, 1fr))` }}
        >
          {activeColumns.map(column => (
            <StoryKanbanColumn
              key={column.status}
              status={column.status}
              label={column.name}
              stories={board[column.status] ?? EMPTY_STORIES}
              lookupIndex={lookupIndex}
              onStoryMove={onStoryMove}
              onStoryClick={onStoryClick}
              isDraggingRef={isDraggingRef}
              pendingMoveRef={pendingMoveRef}
            />
          ))}
        </div>
      </div>
    </div>
  )
}

export default memo(StoryKanbanBoard)
