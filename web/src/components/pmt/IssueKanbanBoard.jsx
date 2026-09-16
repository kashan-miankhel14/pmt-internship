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

const FALLBACK_STATUSES = ['Open', 'InProgress', 'Resolved', 'Closed', 'Rejected']

const FALLBACK_COLUMNS = FALLBACK_STATUSES.map((status, index) => ({
  name: status,
  status,
  ordinal: index,
  completeColumn: status === 'Resolved' || status === 'Closed'
}))

const EMPTY_ISSUES = []
const EMPTY_INDEX = new Map()

const humanize = value => String(value || '').replace(/([a-z])([A-Z])/g, '$1 $2').toUpperCase()

const columnColorMap = {
  Open: { bar: '#64748b', bg: 'rgba(100, 116, 139, 0.05)', border: 'rgba(100, 116, 139, 0.2)' },
  InProgress: { bar: '#3b82f6', bg: 'rgba(59, 130, 246, 0.05)', border: 'rgba(59, 130, 246, 0.2)' },
  Resolved: { bar: '#22c55e', bg: 'rgba(34, 197, 94, 0.05)', border: 'rgba(34, 197, 94, 0.2)' },
  Closed: { bar: '#10b981', bg: 'rgba(16, 185, 129, 0.05)', border: 'rgba(16, 185, 129, 0.2)' },
  Rejected: { bar: '#ef4444', bg: 'rgba(239, 68, 68, 0.05)', border: 'rgba(239, 68, 68, 0.2)' }
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

// Issue Card
const IssueCard = memo(({ issue, lookupIndex, onIssueClick }) => {
  const assignee = lookupIndex.users.get(issue.assignedToUserId)
  const project = lookupIndex.projects.get(issue.projectId)
  const team = lookupIndex.teams?.get(issue.teamId)

  return (
    <div
      className='cursor-grab select-none rounded-lg border border-[var(--mui-palette-divider)] bg-backgroundPaper p-2.5 shadow-xs transition-all duration-200 ease-out hover:border-[var(--mui-palette-primary-main)] hover:shadow-sm active:cursor-grabbing'
      onClick={() => onIssueClick?.(issue)}
    >
      {/* Top row: Key & Severity */}
      <div className='flex items-center justify-between gap-1.5 mb-1.5'>
        <span className='font-mono text-[10px] font-bold px-1.5 py-0.5 rounded bg-error-lighter/15 text-error-main border border-error-light/30'>
          ISSUE-{issue.id}
        </span>
        <StatusChip value={issue.severity} size='small' />
      </div>

      {/* Title */}
      <Typography variant='subtitle2' className='font-semibold text-textPrimary leading-snug mb-1 line-clamp-2 text-xs'>
        {issue.title}
      </Typography>

      {/* Description */}
      {issue.description && (
        <Typography variant='body2' color='text.secondary' className='text-[11px] line-clamp-1 mb-1.5'>
          {issue.description}
        </Typography>
      )}

      {/* Project & Team Tags */}
      {(project || team) && (
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
        </div>
      )}

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

const IssueKanbanColumn = memo(({ status, label, issues, lookupIndex, onIssueMove, onIssueClick, isDraggingRef, pendingMoveRef }) => {
  const onIssueMoveRef = useRef(onIssueMove)

  onIssueMoveRef.current = onIssueMove

  const styleConfig = columnColorMap[status] || { bar: '#ef4444', bg: 'rgba(239, 68, 68, 0.05)', border: 'rgba(239, 68, 68, 0.2)' }

  const [parent, values, setValues] = useDragAndDrop(issues, {
    group: 'pmt-issue-kanban',
    sortable: true,
    onDragstart: () => {
      isDraggingRef.current = true
      pendingMoveRef.current = null
    },
    onDragend: () => {
      isDraggingRef.current = false
      const pending = pendingMoveRef.current

      pendingMoveRef.current = null
      if (pending) onIssueMoveRef.current(pending.issue, pending.toStatus)
    },
    onTransfer: ({ initialParent, targetParent, draggedNodes }) => {
      const toStatus = targetParent?.el?.getAttribute('data-status')
      const fromStatus = initialParent?.el?.getAttribute('data-status')
      const issue = draggedNodes?.[0]?.data?.value

      if (issue && toStatus && fromStatus && toStatus === status && fromStatus !== status) {
        pendingMoveRef.current = { issue, toStatus }
      }
    }
  })

  useEffect(() => {
    if (!isDraggingRef.current) setValues(issues)
  }, [issues, setValues, isDraggingRef])

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
            No issues in {humanize(label ?? status)}
          </div>
        ) : (
          values.map(issue => (
            <IssueCard key={issue.id} issue={issue} lookupIndex={lookupIndex} onIssueClick={onIssueClick} />
          ))
        )}
      </div>
    </div>
  )
})

const IssueKanbanBoard = ({ board, columns, lookups, onIssueMove, onIssueClick, isDraggingRef, pendingMoveRef }) => {
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
            <IssueKanbanColumn
              key={column.status}
              status={column.status}
              label={column.name}
              issues={board[column.status] ?? EMPTY_ISSUES}
              lookupIndex={lookupIndex}
              onIssueMove={onIssueMove}
              onIssueClick={onIssueClick}
              isDraggingRef={isDraggingRef}
              pendingMoveRef={pendingMoveRef}
            />
          ))}
        </div>
      </div>
    </div>
  )
}

export default memo(IssueKanbanBoard)
