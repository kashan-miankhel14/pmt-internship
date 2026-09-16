'use client'

import { useState, useMemo, useRef, useEffect } from 'react'

import Card from '@mui/material/Card'
import CardContent from '@mui/material/CardContent'
import Typography from '@mui/material/Typography'
import Button from '@mui/material/Button'
import IconButton from '@mui/material/IconButton'
import Chip from '@mui/material/Chip'
import MenuItem from '@mui/material/MenuItem'
import Tooltip from '@mui/material/Tooltip'
import Avatar from '@mui/material/Avatar'
import Dialog from '@mui/material/Dialog'
import DialogTitle from '@mui/material/DialogTitle'
import DialogContent from '@mui/material/DialogContent'
import DialogActions from '@mui/material/DialogActions'

import { toast } from 'react-toastify'
import { useMutation, useQueryClient } from '@tanstack/react-query'

import CustomTextField from '@core/components/mui/TextField'

import StatusChip from '@/components/pmt/StatusChip'
import { useBoardColumns } from '@/hooks/useBoardColumns'
import { useSprints } from '@/hooks/useSprints'
import { useLookups } from '@/hooks/useLookups'
import { tasksService } from '@/services/tasks'
import { extractErrors } from '@/libs/errors'
import { priorityLabel, priorityColor } from '@/libs/enums'

const emptyTaskForm = {
  title: '',
  description: '',
  userStoryId: null,
  priority: 3,
  status: 'ToDo',
  estimatedHours: 4,
  assignedToUserId: null,
  teamId: null,
  dueDate: ''
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

export const ProjectActiveBoardView = ({ projectId, projectKey, templateCode, stories = [], tasks = [], onRefresh }) => {
  const queryClient = useQueryClient()
  const { sprints = [] } = useSprints(projectKey)
  const { columns = [] } = useBoardColumns({ projectKey, templateCode, fallback: ['ToDo', 'InProgress', 'Blocked', 'Review', 'Done'] })
  const { data: lookups = {} } = useLookups()

  const [selectedSprintFilter, setSelectedSprintFilter] = useState('ACTIVE_ONLY')
  const [searchQuery, setSearchQuery] = useState('')
  const [assigneeFilter, setAssigneeFilter] = useState('ALL')
  const [teamFilter, setTeamFilter] = useState('ALL')
  const [storyFilter, setStoryFilter] = useState('ALL')
  const [taskModal, setTaskModal] = useState({ open: false, isEdit: false, data: emptyTaskForm })

  const usersList = useMemo(() => lookups.users ?? [], [lookups.users])
  const teamsList = useMemo(() => lookups.teams ?? [], [lookups.teams])

  const userMap = useMemo(() => {
    const map = {}

    usersList.forEach(u => {
      map[u.id] = u.displayName || u.email
    })
    
return map
  }, [usersList])

  const teamMap = useMemo(() => {
    const map = {}

    teamsList.forEach(t => {
      map[t.id] = t.name
    })
    
return map
  }, [teamsList])

  const storyMap = useMemo(() => {
    const map = {}

    stories.forEach(s => {
      map[s.id] = s
    })
    
return map
  }, [stories])

  // Active Sprint
  const activeSprint = useMemo(() => {
    return sprints.find(s => {
      const status = String(s.status || '').toLowerCase()

      
return status.includes('active') || status.includes('progress')
    })
  }, [sprints])

  const topScrollRef = useRef(null)
  const bottomScrollRef = useRef(null)
  const [scrollWidth, setScrollWidth] = useState(0)

  useEffect(() => {
    const updateWidth = () => {
      if (bottomScrollRef.current) {
        setScrollWidth(bottomScrollRef.current.scrollWidth)
      }
    }

    updateWidth()
    window.addEventListener('resize', updateWidth)
    
return () => window.removeEventListener('resize', updateWidth)
  }, [columns, tasks])

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

  const invalidateAll = () => {
    queryClient.invalidateQueries({ queryKey: ['project-detail', projectId] })
    if (onRefresh) onRefresh()
  }

  // Update Task Mutation (Status transition / edit)
  const updateTaskMutation = useMutation({
    mutationFn: ({ id, data }) => tasksService.update(id, data),
    onSuccess: () => {
      toast.success('Task updated')
      invalidateAll()
    },
    onError: error => toast.error(extractErrors(error.response?.data)[0] || 'Failed to update task')
  })

  // Create Task Mutation
  const createTaskMutation = useMutation({
    mutationFn: data => tasksService.create({ ...data, projectId }),
    onSuccess: () => {
      toast.success('Task created')
      setTaskModal({ open: false, isEdit: false, data: emptyTaskForm })
      invalidateAll()
    },
    onError: error => toast.error(extractErrors(error.response?.data)[0] || 'Failed to create task')
  })

  // Delete Task Mutation
  const deleteTaskMutation = useMutation({
    mutationFn: id => tasksService.remove(id),
    onSuccess: () => {
      toast.success('Task deleted')
      invalidateAll()
    },
    onError: error => toast.error(extractErrors(error.response?.data)[0] || 'Failed to delete task')
  })

  // Filter Tasks
  const filteredTasks = useMemo(() => {
    return tasks.filter(task => {
      const parentStory = storyMap[task.userStoryId || task.storyId]

      // Sprint scope filter
      if (selectedSprintFilter === 'ACTIVE_ONLY') {
        if (activeSprint) {
          if (!parentStory || parentStory.sprintId !== activeSprint.id) return false
        }
      } else if (selectedSprintFilter !== 'ALL') {
        const sId = Number(selectedSprintFilter)

        if (!parentStory || parentStory.sprintId !== sId) return false
      }

      // Assignee filter
      if (assigneeFilter !== 'ALL') {
        if (String(task.assignedToUserId || task.assigneeUserId) !== String(assigneeFilter)) return false
      }

      // Team filter
      if (teamFilter !== 'ALL') {
        if (String(task.teamId) !== String(teamFilter)) return false
      }

      // Parent Story filter
      if (storyFilter !== 'ALL') {
        if (String(task.userStoryId || task.storyId) !== String(storyFilter)) return false
      }

      // Search query
      if (searchQuery) {
        const q = searchQuery.toLowerCase()
        const matchTitle = task.title?.toLowerCase().includes(q)
        const matchDesc = task.description?.toLowerCase().includes(q)
        const matchStory = parentStory?.title?.toLowerCase().includes(q)

        if (!matchTitle && !matchDesc && !matchStory) return false
      }

      return true
    })
  }, [tasks, storyMap, selectedSprintFilter, activeSprint, assigneeFilter, teamFilter, storyFilter, searchQuery])

  // Normalise status matching
  const matchTaskStatusToColumn = (taskStatus, columnStatus) => {
    const t = String(taskStatus || '').replace(/[^a-zA-Z0-9]/g, '').toLowerCase()
    const c = String(columnStatus || '').replace(/[^a-zA-Z0-9]/g, '').toLowerCase()

    if (t === c) return true
    if ((t === 'todo' || t === 'backlog' || t === 'ready') && (c === 'todo' || c === 'backlog' || c === 'selected')) return true
    if ((t === 'inprogress' || t === 'doing' || t === 'active') && (c === 'inprogress' || c === 'doing')) return true
    if ((t === 'review' || t === 'inreview' || t === 'qa') && (c === 'review' || c === 'inreview' || c === 'qa')) return true
    if ((t === 'done' || t === 'completed' || t === 'closed') && (c === 'done' || c === 'complete' || c === 'closed')) return true

    return false
  }

  return (
    <div className='flex flex-col gap-6'>
      {/* Board Header & Controls */}
      <div className='flex flex-wrap items-center justify-between gap-4 bg-backgroundPaper p-4 rounded-xl border border-[var(--mui-palette-divider)] shadow-sm'>
        <div className='flex flex-wrap items-center gap-3 flex-1 min-w-[300px]'>
          {/* Sprint Selector */}
          <CustomTextField
            select
            size='small'
            label='Sprint Scope'
            value={selectedSprintFilter}
            onChange={e => setSelectedSprintFilter(e.target.value)}
            className='w-52'
          >
            <MenuItem value='ACTIVE_ONLY'>
              {activeSprint ? `▶ Active: ${activeSprint.name}` : '⚡ All Work Items'}
            </MenuItem>
            <MenuItem value='ALL'>🌐 All Sprints & Backlog</MenuItem>
            {sprints.map(s => (
              <MenuItem key={s.id} value={String(s.id)}>
                🏃 {s.name} ({s.status})
              </MenuItem>
            ))}
          </CustomTextField>

          {/* Search Filter */}
          <CustomTextField
            size='small'
            placeholder='Search board tasks…'
            value={searchQuery}
            onChange={e => setSearchQuery(e.target.value)}
            className='w-60'
            InputProps={{
              startAdornment: <i className='tabler-search text-textSecondary mr-2 text-sm' />
            }}
          />

          {/* Assignee Filter */}
          <CustomTextField
            select
            size='small'
            value={assigneeFilter}
            onChange={e => setAssigneeFilter(e.target.value)}
            className='w-40'
          >
            <MenuItem value='ALL'>All Assignees</MenuItem>
            {usersList.map(u => (
              <MenuItem key={u.id} value={String(u.id)}>
                {u.displayName || u.email}
              </MenuItem>
            ))}
          </CustomTextField>

          {/* Team Filter */}
          <CustomTextField
            select
            size='small'
            value={teamFilter}
            onChange={e => setTeamFilter(e.target.value)}
            className='w-40'
          >
            <MenuItem value='ALL'>All Teams</MenuItem>
            {teamsList.map(t => (
              <MenuItem key={t.id} value={String(t.id)}>
                👥 {t.name}
              </MenuItem>
            ))}
          </CustomTextField>

          {/* Parent Story Filter */}
          <CustomTextField
            select
            size='small'
            value={storyFilter}
            onChange={e => setStoryFilter(e.target.value)}
            className='w-48'
          >
            <MenuItem value='ALL'>All User Stories</MenuItem>
            {stories.map(s => (
              <MenuItem key={s.id} value={String(s.id)}>
                STORY-{s.id}: {s.title}
              </MenuItem>
            ))}
          </CustomTextField>
        </div>

        <Button
          variant='contained'
          size='small'
          className='font-semibold shadow-sm'
          startIcon={<i className='tabler-plus' />}
          onClick={() => setTaskModal({ open: true, isEdit: false, data: emptyTaskForm })}
        >
          Create Task
        </Button>
      </div>

      {/* Active Sprint Banner */}
      {activeSprint && selectedSprintFilter === 'ACTIVE_ONLY' && (
        <div className='flex flex-wrap items-center justify-between gap-2 p-3.5 rounded-xl bg-primary-lighter/10 border border-primary-light/30 text-sm'>
          <div className='flex items-center gap-2'>
            <i className='tabler-flag-filled text-primary-main' />
            <span className='font-bold text-primary-dark dark:text-primary-light'>
              Active Sprint: {activeSprint.name}
            </span>
            {activeSprint.goal && <span className='text-textSecondary'>— &quot;{activeSprint.goal}&quot;</span>}
          </div>
          <Typography variant='caption' className='text-textSecondary font-mono font-medium'>
            {activeSprint.startDate ? new Date(activeSprint.startDate).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' }) : 'TBD'}
            {' → '}
            {activeSprint.endDate ? new Date(activeSprint.endDate).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' }) : 'TBD'}
          </Typography>
        </div>
      )}

      {/* Top Scrollbar */}
      <div
        ref={topScrollRef}
        onScroll={handleTopScroll}
        className='w-full overflow-x-auto h-2.5 mb-2 top-scrollbar rounded bg-actionHover/30 border border-[var(--mui-palette-divider)]'
      >
        <div style={{ width: scrollWidth || '100%', height: 1 }} />
      </div>

      {/* The Scrum / Kanban Board Columns - Bottom scrollbar hidden */}
      <div
        ref={bottomScrollRef}
        onScroll={handleBottomScroll}
        className='w-full overflow-x-auto no-scrollbar pb-2'
      >
        <div
          className='grid gap-2.5 items-start min-w-full'
          style={{ gridTemplateColumns: `repeat(${columns.length}, minmax(240px, 1fr))` }}
        >
          {columns.map(column => {
            const colStatus = column.status
            const colTasks = filteredTasks.filter(t => matchTaskStatusToColumn(t.status, colStatus))

            const colNorm = String(colStatus || '').toLowerCase().replace(/[^a-z]/g, '')

            const colBar = colNorm.includes('done') || colNorm.includes('complete')
              ? '#22c55e'
              : colNorm.includes('review') || colNorm.includes('qa')
                ? '#a855f7'
                : colNorm.includes('progress') || colNorm.includes('doing') || colNorm.includes('active')
                  ? '#3b82f6'
                  : '#64748b'

            return (
              <Card
                key={column.name}
                className='flex flex-col border border-[var(--mui-palette-divider)] bg-actionHover/30 shadow-xs min-h-[400px] overflow-hidden rounded-xl'
              >
                {/* Column Header */}
                <div
                  className='flex items-center justify-between p-3 border-b border-[var(--mui-palette-divider)] bg-backgroundPaper'
                  style={{ borderTop: `3px solid ${colBar}` }}
                >
                  <div className='flex items-center gap-2'>
                    <Typography variant='subtitle2' className='font-bold uppercase tracking-wider text-xs text-textPrimary'>
                      {column.name}
                    </Typography>
                    <span
                      className='flex items-center justify-center min-w-[20px] h-[20px] px-1 rounded-full text-[11px] font-bold'
                      style={{ backgroundColor: colBar, color: '#ffffff' }}
                    >
                      {colTasks.length}
                    </span>
                  </div>
                  <IconButton
                    size='small'
                    onClick={() => setTaskModal({ open: true, isEdit: false, data: { ...emptyTaskForm, status: colStatus } })}
                    aria-label={`Add task in ${column.name}`}
                    className='text-textSecondary hover:text-textPrimary'
                  >
                    <i className='tabler-plus text-xs' />
                  </IconButton>
                </div>

                {/* Column Cards Container with vertical scroll */}
                <CardContent className='flex-1 flex flex-col gap-2 p-2.5 max-h-[calc(100vh-290px)] overflow-y-auto'>
                {colTasks.length === 0 ? (
                  <div className='py-12 border-2 border-dashed border-[var(--mui-palette-divider)] rounded-lg text-center bg-backgroundPaper/50'>
                    <Typography variant='caption' className='text-textDisabled font-medium'>
                      No tasks in {column.name}
                    </Typography>
                  </div>
                ) : (
                  colTasks.map(task => {
                    const parentStory = storyMap[task.userStoryId || task.storyId]
                    const assigneeName = userMap[task.assignedToUserId || task.assigneeUserId] || 'Unassigned'
                    const isOverdue = task.dueDate && new Date(task.dueDate) < new Date() && !column.completeColumn

                    return (
                      <div
                        key={task.id}
                        className='bg-backgroundPaper rounded-lg p-3.5 border border-[var(--mui-palette-divider)] shadow-xs hover:shadow-md hover:border-primary-main/60 transition-all flex flex-col gap-2 cursor-pointer select-none'
                        onClick={() => setTaskModal({ open: true, isEdit: true, data: { ...task, userStoryId: task.userStoryId || task.storyId } })}
                      >
                        {/* Parent Story Tag & Priority */}
                        <div className='flex items-center justify-between gap-1.5'>
                          {parentStory ? (
                            <span className='inline-flex items-center gap-1 text-[11px] text-primary-main font-semibold bg-primary-lighter/15 px-2 py-0.5 rounded-md truncate max-w-[180px] border border-primary-light/20'>
                              <i className='tabler-bookmark text-xs' />
                              <span className='truncate'>{parentStory.title}</span>
                            </span>
                          ) : (
                            <span className='text-[11px] font-mono text-textDisabled'>#{task.id}</span>
                          )}
                          <StatusChip value={priorityLabel(task.priority)} size='small' />
                        </div>

                        {/* Task Title */}
                        <Typography className='font-semibold text-sm leading-snug text-textPrimary line-clamp-2'>
                          {task.title}
                        </Typography>

                        {/* Task Description snippet */}
                        {task.description && (
                          <Typography variant='body2' className='text-xs text-textSecondary line-clamp-2'>
                            {task.description}
                          </Typography>
                        )}

                        {/* Card Footer: Hours, Team & Assignee */}
                        <div className='flex items-center justify-between gap-2 pt-2 border-t border-[var(--mui-palette-divider)] mt-1' onClick={e => e.stopPropagation()}>
                          <div className='flex items-center gap-1.5 flex-wrap'>
                            {(() => {
                              const isDone = String(task.status).toLowerCase().includes('done')

                              
return (
                                <Tooltip title={isDone ? 'Reopen task (To Do)' : 'Mark task Done'}>
                                  <IconButton
                                    size='small'
                                    className='p-0.5'
                                    onClick={e => {
                                      e.stopPropagation()
                                      updateTaskMutation.mutate({
                                        id: task.id,
                                        data: { ...task, status: isDone ? 'ToDo' : 'Done', projectId }
                                      })
                                    }}
                                  >
                                    {isDone ? (
                                      <i className='tabler-circle-check-filled text-success-main text-sm' />
                                    ) : (
                                      <i className='tabler-circle text-textDisabled hover:text-success-main text-sm' />
                                    )}
                                  </IconButton>
                                </Tooltip>
                              )
                            })()}

                            {task.estimatedHours != null && (
                              <Tooltip title='Estimated Hours'>
                                <span className='text-[11px] font-mono text-textSecondary bg-actionHover border border-[var(--mui-palette-divider)] px-1.5 py-0.5 rounded'>
                                  ⏱ {task.estimatedHours}h
                                </span>
                              </Tooltip>
                            )}

                            {task.teamId && teamMap[task.teamId] && (
                              <span className='text-[10px] text-secondary-dark bg-secondary-lighter/20 border border-secondary-light/30 px-1.5 py-0.5 rounded font-medium'>
                                👥 {teamMap[task.teamId]}
                              </span>
                            )}

                            {isOverdue && (
                              <Tooltip title='Task is overdue!'>
                                <span className='text-[10px] font-bold px-1.5 py-0.5 rounded bg-error-lighter/20 text-error-main border border-error-main/30'>
                                  OVERDUE
                                </span>
                              </Tooltip>
                            )}
                          </div>

                          <div className='flex items-center gap-1.5'>
                            <Tooltip title={`Assignee: ${assigneeName}`}>
                              <div className='flex items-center gap-1 bg-actionHover border border-[var(--mui-palette-divider)] rounded-full px-2 py-0.5'>
                                <Avatar sx={{ width: 18, height: 18, fontSize: 9, bgcolor: 'primary.main', fontWeight: 600 }}>
                                  {getInitials(assigneeName)}
                                </Avatar>
                                <span className='text-[11px] text-textPrimary max-w-[80px] truncate font-medium'>
                                  {assigneeName.split(' ')[0]}
                                </span>
                              </div>
                            </Tooltip>
                          </div>
                        </div>
                      </div>
                    )
                  })
                )}
              </CardContent>
            </Card>
          )
        })}
        </div>
      </div>

      {/* CREATE / EDIT TASK MODAL */}
      <Dialog open={taskModal.open} onClose={() => setTaskModal({ open: false, isEdit: false, data: emptyTaskForm })} fullWidth maxWidth='sm'>
        <DialogTitle>{taskModal.isEdit ? `Edit Task #${taskModal.data?.id}` : 'Create New Task'}</DialogTitle>
        <DialogContent className='flex flex-col gap-4 pt-2'>
          <CustomTextField
            label='Task Title'
            placeholder='e.g., Implement JWT refresh token rotation'
            fullWidth
            required
            value={taskModal.data.title}
            onChange={e => setTaskModal({ ...taskModal, data: { ...taskModal.data, title: e.target.value } })}
          />
          <CustomTextField
            select
            label='Parent User Story / Feature'
            fullWidth
            value={taskModal.data.userStoryId ? String(taskModal.data.userStoryId) : ''}
            onChange={e => setTaskModal({ ...taskModal, data: { ...taskModal.data, userStoryId: e.target.value ? Number(e.target.value) : null } })}
          >
            <MenuItem value=''>None (Stand-alone Task)</MenuItem>
            {stories.map(s => (
              <MenuItem key={s.id} value={String(s.id)}>
                STORY-{s.id}: {s.title}
              </MenuItem>
            ))}
          </CustomTextField>
          <CustomTextField
            label='Description / Technical Notes'
            placeholder='Details, endpoints, or requirements…'
            fullWidth
            multiline
            rows={3}
            value={taskModal.data.description}
            onChange={e => setTaskModal({ ...taskModal, data: { ...taskModal.data, description: e.target.value } })}
          />
          <div className='grid grid-cols-2 gap-4'>
            <CustomTextField
              select
              label='Status Column'
              fullWidth
              value={taskModal.data.status || 'ToDo'}
              onChange={e => setTaskModal({ ...taskModal, data: { ...taskModal.data, status: e.target.value } })}
            >
              {columns.map(c => (
                <MenuItem key={c.name} value={c.status}>
                  {c.name}
                </MenuItem>
              ))}
            </CustomTextField>
            <CustomTextField
              select
              label='Priority'
              fullWidth
              value={taskModal.data.priority}
              onChange={e => setTaskModal({ ...taskModal, data: { ...taskModal.data, priority: Number(e.target.value) } })}
            >
              <MenuItem value={1}>Highest</MenuItem>
              <MenuItem value={2}>High</MenuItem>
              <MenuItem value={3}>Medium</MenuItem>
              <MenuItem value={4}>Low</MenuItem>
              <MenuItem value={5}>Lowest</MenuItem>
            </CustomTextField>
          </div>
          <div className='grid grid-cols-2 sm:grid-cols-4 gap-4'>
            <CustomTextField
              label='Estimated Hours'
              type='number'
              fullWidth
              value={taskModal.data.estimatedHours}
              onChange={e => setTaskModal({ ...taskModal, data: { ...taskModal.data, estimatedHours: Number(e.target.value) } })}
            />
            <CustomTextField
              label='Actual Hours Spent'
              type='number'
              fullWidth
              value={taskModal.data.actualHours ?? ''}
              onChange={e => setTaskModal({ ...taskModal, data: { ...taskModal.data, actualHours: e.target.value ? Number(e.target.value) : null } })}
            />
            <CustomTextField
              select
              label='Assignee'
              fullWidth
              value={taskModal.data.assignedToUserId ? String(taskModal.data.assignedToUserId) : ''}
              onChange={e => setTaskModal({ ...taskModal, data: { ...taskModal.data, assignedToUserId: e.target.value ? Number(e.target.value) : null } })}
            >
              <MenuItem value=''>None / Unassigned</MenuItem>
              {usersList.map(u => (
                <MenuItem key={u.id} value={String(u.id)}>
                  {u.displayName || u.email}
                </MenuItem>
              ))}
            </CustomTextField>
            <CustomTextField
              select
              label='Team'
              fullWidth
              value={taskModal.data.teamId ? String(taskModal.data.teamId) : ''}
              onChange={e => setTaskModal({ ...taskModal, data: { ...taskModal.data, teamId: e.target.value ? Number(e.target.value) : null } })}
            >
              <MenuItem value=''>None / Unassigned</MenuItem>
              {teamsList.map(t => (
                <MenuItem key={t.id} value={String(t.id)}>
                  👥 {t.name}
                </MenuItem>
              ))}
            </CustomTextField>
          </div>
          <CustomTextField
            label='Due Date'
            type='date'
            fullWidth
            InputLabelProps={{ shrink: true }}
            value={taskModal.data.dueDate ? taskModal.data.dueDate.substring(0, 10) : ''}
            onChange={e => setTaskModal({ ...taskModal, data: { ...taskModal.data, dueDate: e.target.value } })}
          />
        </DialogContent>
        <DialogActions>
          {taskModal.isEdit && (
            <Button
              color='error'
              className='mr-auto'
              onClick={() => {
                deleteTaskMutation.mutate(taskModal.data.id)
                setTaskModal({ open: false, isEdit: false, data: emptyTaskForm })
              }}
            >
              Delete
            </Button>
          )}
          <Button onClick={() => setTaskModal({ open: false, isEdit: false, data: emptyTaskForm })}>Cancel</Button>
          <Button
            variant='contained'
            disabled={!taskModal.data.title || createTaskMutation.isPending || updateTaskMutation.isPending}
            onClick={() => {
              if (taskModal.isEdit) {
                updateTaskMutation.mutate({ id: taskModal.data.id, data: { ...taskModal.data, projectId } })
                setTaskModal({ open: false, isEdit: false, data: emptyTaskForm })
              } else {
                createTaskMutation.mutate(taskModal.data)
              }
            }}
          >
            {taskModal.isEdit ? 'Update Task' : 'Create Task'}
          </Button>
        </DialogActions>
      </Dialog>
    </div>
  )
}

export default ProjectActiveBoardView
