'use client'

import { useState, useMemo } from 'react'

import Card from '@mui/material/Card'
import CardContent from '@mui/material/CardContent'
import Typography from '@mui/material/Typography'
import Button from '@mui/material/Button'
import IconButton from '@mui/material/IconButton'
import Chip from '@mui/material/Chip'
import Collapse from '@mui/material/Collapse'
import Dialog from '@mui/material/Dialog'
import DialogTitle from '@mui/material/DialogTitle'
import DialogContent from '@mui/material/DialogContent'
import DialogActions from '@mui/material/DialogActions'
import MenuItem from '@mui/material/MenuItem'
import Tooltip from '@mui/material/Tooltip'
import Avatar from '@mui/material/Avatar'
import LinearProgress from '@mui/material/LinearProgress'

import { toast } from 'react-toastify'
import { useMutation, useQueryClient } from '@tanstack/react-query'

import CustomTextField from '@core/components/mui/TextField'

import StatusChip from '@/components/pmt/StatusChip'
import { useSprints } from '@/hooks/useSprints'
import { useLookups } from '@/hooks/useLookups'
import { storiesService } from '@/services/stories'
import { tasksService } from '@/services/tasks'
import { extractErrors } from '@/libs/errors'
import { priorityLabel, priorityColor } from '@/libs/enums'

const emptySprintForm = { name: '', goal: '', startDate: '', endDate: '' }
const emptyStoryForm = { title: '', description: '', acceptanceCriteria: '', priority: 3, storyPoints: 3, sprintId: null, assignedToUserId: null, teamId: null }
const emptyTaskForm = { title: '', description: '', userStoryId: null, priority: 3, status: 'ToDo', estimatedHours: 4, assignedToUserId: null, teamId: null, dueDate: '' }

const getInitials = name => {
  if (!name) return '?'
  
return name
    .split(' ')
    .map(n => n[0])
    .join('')
    .substring(0, 2)
    .toUpperCase()
}

export const ProjectBacklogView = ({ projectId, projectKey, stories = [], tasks = [], onRefresh }) => {
  const queryClient = useQueryClient()
  const { sprints = [], startMutation, completeMutation, createMutation, deleteMutation } = useSprints(projectKey)
  const { data: lookups = {} } = useLookups()

  const [searchQuery, setSearchQuery] = useState('')
  const [assigneeFilter, setAssigneeFilter] = useState('ALL')
  const [teamFilter, setTeamFilter] = useState('ALL')
  const [expandedStories, setExpandedStories] = useState({})

  // Dialog states
  const [sprintModal, setSprintModal] = useState({ open: false, isEdit: false, data: emptySprintForm })
  const [storyModal, setStoryModal] = useState({ open: false, isEdit: false, data: emptyStoryForm })
  const [taskModal, setTaskModal] = useState({ open: false, isEdit: false, storyId: null, data: emptyTaskForm })
  const [confirmSprint, setConfirmSprint] = useState({ open: false, type: '', sprint: null })

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

  // Invalidate queries after mutations
  const invalidateAll = () => {
    queryClient.invalidateQueries({ queryKey: ['project-detail', projectId] })
    queryClient.invalidateQueries({ queryKey: ['project-sprints', projectKey] })
    if (onRefresh) onRefresh()
  }

  // Story mutations
  const updateStoryMutation = useMutation({
    mutationFn: ({ id, data }) => storiesService.update(id, data),
    onSuccess: () => {
      toast.success('Story updated')
      invalidateAll()
    },
    onError: error => toast.error(extractErrors(error.response?.data)[0] || 'Failed to update story')
  })

  const createStoryMutation = useMutation({
    mutationFn: data => storiesService.create({ ...data, projectId }),
    onSuccess: () => {
      toast.success('User Story created')
      setStoryModal({ open: false, isEdit: false, data: emptyStoryForm })
      invalidateAll()
    },
    onError: error => toast.error(extractErrors(error.response?.data)[0] || 'Failed to create story')
  })

  const deleteStoryMutation = useMutation({
    mutationFn: id => storiesService.remove(id),
    onSuccess: () => {
      toast.success('Story deleted')
      invalidateAll()
    },
    onError: error => toast.error(extractErrors(error.response?.data)[0] || 'Failed to delete story')
  })

  // Task creation mutation
  const createTaskMutation = useMutation({
    mutationFn: data => tasksService.create({ ...data, projectId }),
    onSuccess: () => {
      toast.success('Task added to User Story')
      setTaskModal({ open: false, isEdit: false, storyId: null, data: emptyTaskForm })
      invalidateAll()
    },
    onError: error => toast.error(extractErrors(error.response?.data)[0] || 'Failed to create task')
  })

  // Task update mutation
  const updateTaskMutation = useMutation({
    mutationFn: ({ id, data }) => tasksService.update(id, data),
    onSuccess: () => {
      toast.success('Task updated')
      invalidateAll()
    },
    onError: error => toast.error(extractErrors(error.response?.data)[0] || 'Failed to update task')
  })

  const toggleStoryExpand = storyId => {
    setExpandedStories(prev => ({ ...prev, [storyId]: !prev[storyId] }))
  }

  // Filter stories
  const filteredStories = useMemo(() => {
    return stories.filter(story => {
      const matchesSearch = !searchQuery || story.title?.toLowerCase().includes(searchQuery.toLowerCase()) || story.description?.toLowerCase().includes(searchQuery.toLowerCase())
      const matchesAssignee = assigneeFilter === 'ALL' || String(story.assignedToUserId || story.assigneeUserId) === String(assigneeFilter)
      const matchesTeam = teamFilter === 'ALL' || String(story.teamId) === String(teamFilter)

      
return matchesSearch && matchesAssignee && matchesTeam
    })
  }, [stories, searchQuery, assigneeFilter, teamFilter])

  // Group stories by Sprint
  const { activeSprints, plannedSprints, completedSprints, backlogStories } = useMemo(() => {
    const active = []
    const planned = []
    const completed = []
    const backlog = []

    const sprintMap = {}

    sprints.forEach(s => {
      const status = String(s.status || '').toLowerCase()

      if (status.includes('active') || status.includes('progress')) {
        active.push({ ...s, stories: [] })
      } else if (status.includes('complete') || status.includes('closed') || status.includes('done')) {
        completed.push({ ...s, stories: [] })
      } else {
        planned.push({ ...s, stories: [] })
      }

      sprintMap[s.id] = true
    })

    filteredStories.forEach(story => {
      const sId = story.sprintId

      if (sId && sprintMap[sId]) {
        const inActive = active.find(s => s.id === sId)

        if (inActive) {
          inActive.stories.push(story)
          
return
        }

        const inPlanned = planned.find(s => s.id === sId)

        if (inPlanned) {
          inPlanned.stories.push(story)
          
return
        }

        const inCompleted = completed.find(s => s.id === sId)

        if (inCompleted) {
          inCompleted.stories.push(story)
          
return
        }
      }

      backlog.push(story)
    })

    return { activeSprints: active, plannedSprints: planned, completedSprints: completed, backlogStories: backlog }
  }, [sprints, filteredStories])

  // Story tasks map
  const storyTasksMap = useMemo(() => {
    const map = {}

    tasks.forEach(t => {
      const parentId = t.userStoryId || t.storyId

      if (parentId) {
        if (!map[parentId]) map[parentId] = []
        map[parentId].push(t)
      }
    })
    
return map
  }, [tasks])

  const handleMoveStorySprint = (story, targetSprintId) => {
    updateStoryMutation.mutate({
      id: story.id,
      data: {
        ...story,
        projectId,
        sprintId: targetSprintId ? Number(targetSprintId) : null
      }
    })
  }

  const handleCreateSprint = async () => {
    try {
      await createMutation.mutateAsync(sprintModal.data)
      toast.success('Sprint created')
      setSprintModal({ open: false, isEdit: false, data: emptySprintForm })
      invalidateAll()
    } catch (error) {
      toast.error(extractErrors(error.response?.data)[0] || 'Failed to create sprint')
    }
  }

  const handleStartSprintAction = async sprint => {
    try {
      await startMutation.mutateAsync(sprint.id)
      toast.success(`Sprint "${sprint.name}" is now Active!`)
      setConfirmSprint({ open: false, type: '', sprint: null })
      invalidateAll()
    } catch (error) {
      toast.error(extractErrors(error.response?.data)[0] || 'Failed to start sprint')
    }
  }

  const handleCompleteSprintAction = async sprint => {
    try {
      await completeMutation.mutateAsync(sprint.id)
      toast.success(`Sprint "${sprint.name}" marked as Completed!`)
      setConfirmSprint({ open: false, type: '', sprint: null })
      invalidateAll()
    } catch (error) {
      toast.error(extractErrors(error.response?.data)[0] || 'Failed to complete sprint')
    }
  }

  // Render individual Sprint Box
  const renderSprintBox = (sprint, type = 'planned') => {
    const sprintStories = sprint.stories || []
    const totalPoints = sprintStories.reduce((sum, s) => sum + Number(s.storyPoints || 0), 0)
    const completedStories = sprintStories.filter(s => String(s.status).toLowerCase() === 'done').length
    const progressPercent = sprintStories.length ? Math.round((completedStories / sprintStories.length) * 100) : 0

    const isActive = type === 'active'

    return (
      <Card
        key={sprint.id}
        className={`border transition-all shadow-sm bg-backgroundPaper overflow-hidden ${
          isActive ? 'border-primary-main ring-2 ring-primary-main/20' : 'border-[var(--mui-palette-divider)]'
        }`}
      >
        {/* Sprint Header Bar */}
        <div className={`p-4 border-b border-[var(--mui-palette-divider)] ${isActive ? 'bg-primary-lighter/10' : 'bg-actionHover/40'}`}>
          <div className='flex flex-wrap items-center justify-between gap-3'>
            <div className='flex items-center gap-3'>
              <div className={`w-9 h-9 rounded-lg flex items-center justify-center font-bold ${isActive ? 'bg-primary-main text-white shadow-xs' : 'bg-actionHover text-textSecondary'}`}>
                <i className={isActive ? 'tabler-player-play-filled text-base' : 'tabler-clock text-base'} />
              </div>
              <div>
                <div className='flex items-center gap-2.5'>
                  <Typography variant='subtitle1' className='font-bold text-textPrimary text-base'>
                    {sprint.name}
                  </Typography>
                  <StatusChip value={isActive ? 'Active' : sprint.status || 'Planning'} size='small' />
                </div>
                <Typography variant='caption' className='text-textSecondary font-medium'>
                  {sprint.startDate && sprint.endDate
                    ? `📅 ${new Date(sprint.startDate).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })} → ${new Date(sprint.endDate).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })}`
                    : 'Dates not scheduled'}
                </Typography>
              </div>
            </div>

            <div className='flex flex-wrap items-center gap-2'>
              <span className='inline-flex items-center gap-1 rounded-md bg-actionHover border border-[var(--mui-palette-divider)] px-2.5 py-1 text-xs font-semibold text-textSecondary'>
                <i className='tabler-calculator text-xs' />
                {totalPoints} Story Points
              </span>
              <span className='inline-flex items-center gap-1 rounded-md bg-actionHover border border-[var(--mui-palette-divider)] px-2.5 py-1 text-xs font-semibold text-textSecondary'>
                {sprintStories.length} Stories
              </span>

              {isActive ? (
                <Button
                  size='small'
                  variant='contained'
                  color='success'
                  startIcon={<i className='tabler-check text-sm' />}
                  onClick={() => setConfirmSprint({ open: true, type: 'complete', sprint })}
                >
                  Complete Sprint
                </Button>
              ) : (
                <Button
                  size='small'
                  variant='contained'
                  color='primary'
                  startIcon={<i className='tabler-player-play text-sm' />}
                  onClick={() => setConfirmSprint({ open: true, type: 'start', sprint })}
                >
                  Start Sprint
                </Button>
              )}

              <IconButton
                size='small'
                color='error'
                onClick={() => deleteMutation.mutate(sprint.id)}
                aria-label={`Delete sprint ${sprint.name}`}
              >
                <i className='tabler-trash text-sm' />
              </IconButton>
            </div>
          </div>

          {/* Goal & Progress */}
          {sprint.goal && (
            <div className='mt-2 text-[11px] text-textPrimary bg-backgroundPaper p-2 rounded-md border border-[var(--mui-palette-divider)] shadow-xs'>
              🎯 <strong>Goal:</strong> {sprint.goal}
            </div>
          )}

          {sprintStories.length > 0 && (
            <div className='flex items-center gap-2.5 mt-2'>
              <LinearProgress
                variant='determinate'
                value={progressPercent}
                className='flex-1 h-1.5 rounded-full bg-actionHover'
                color={progressPercent === 100 ? 'success' : 'primary'}
              />
              <Typography variant='caption' className='font-bold text-[11px] text-textSecondary whitespace-nowrap'>
                {completedStories}/{sprintStories.length} stories done ({progressPercent}%)
              </Typography>
            </div>
          )}
        </div>

        {/* Stories List inside Sprint */}
        <CardContent className='p-3 bg-backgroundPaper flex flex-col gap-2'>
          {sprintStories.length === 0 ? (
            <div className='py-5 border border-dashed border-[var(--mui-palette-divider)] rounded-lg text-center bg-actionHover/15'>
              <i className='tabler-clipboard-list text-2xl text-textDisabled mb-1' />
              <Typography variant='body2' className='text-textSecondary font-medium text-xs'>
                No user stories in this sprint yet.
              </Typography>
              <Typography variant='caption' className='text-textDisabled block mb-2 text-[11px]'>
                Assign stories from the Product Backlog below.
              </Typography>
              <Button
                size='small'
                variant='outlined'
                startIcon={<i className='tabler-plus text-xs' />}
                className='text-xs py-0.5 px-2.5'
                onClick={() => setStoryModal({ open: true, isEdit: false, data: { ...emptyStoryForm, sprintId: sprint.id } })}
              >
                Create Story in {sprint.name}
              </Button>
            </div>
          ) : (
            sprintStories.map(story => renderStoryCard(story))
          )}

          {sprintStories.length > 0 && (
            <Button
              size='small'
              variant='text'
              className='self-start text-primary-main font-semibold text-xs py-0.5 mt-0.5'
              startIcon={<i className='tabler-plus text-xs' />}
              onClick={() => setStoryModal({ open: true, isEdit: false, data: { ...emptyStoryForm, sprintId: sprint.id } })}
            >
              Add Story to {sprint.name}
            </Button>
          )}
        </CardContent>
      </Card>
    )
  }

  // Render individual Story Row
  const renderStoryCard = story => {
    const childTasks = storyTasksMap[story.id] || []
    const isExpanded = Boolean(expandedStories[story.id])
    const assigneeName = userMap[story.assignedToUserId || story.assigneeUserId] || 'Unassigned'
    const completedTasksCount = childTasks.filter(t => String(t.status).toLowerCase().includes('done')).length

    return (
      <div
        key={story.id}
        className='border border-[var(--mui-palette-divider)] bg-backgroundPaper rounded-lg p-2.5 shadow-xs hover:border-primary-main/60 hover:shadow-xs transition-all'
      >
        <div className='flex flex-wrap items-center justify-between gap-2'>
          <div className='flex items-center gap-2 flex-1 min-w-[260px]'>
            <IconButton size='small' onClick={() => toggleStoryExpand(story.id)} aria-label='Toggle tasks' className='text-textSecondary p-1'>
              <i className={isExpanded ? 'tabler-chevron-down text-sm' : 'tabler-chevron-right text-sm'} />
            </IconButton>

            <span className='font-mono text-[10px] font-bold px-1.5 py-0.5 rounded bg-primary-lighter/20 text-primary-main border border-primary-light/30'>
              STORY-{story.id}
            </span>

            <Typography className='font-semibold text-xs text-textPrimary'>{story.title}</Typography>

            {childTasks.length > 0 && (
              <span className='inline-flex items-center px-1.5 py-0.5 rounded-full text-[10px] font-medium bg-actionHover text-textSecondary border border-[var(--mui-palette-divider)]'>
                {completedTasksCount}/{childTasks.length} tasks
              </span>
            )}
          </div>

          <div className='flex items-center gap-2 flex-wrap'>
            {/* Story Points */}
            <Tooltip title='Story Points'>
              <span className='font-bold text-[11px] px-1.5 py-0.5 rounded bg-purple-500/10 text-purple-600 border border-purple-500/20'>
                {story.storyPoints ?? 0} pts
              </span>
            </Tooltip>

            {/* Priority */}
            <StatusChip value={priorityLabel(story.priority)} size='small' />

            {/* Status */}
            <StatusChip value={story.status} size='small' />

            {/* Assignee */}
            <Tooltip title={`Assignee: ${assigneeName}`}>
              <div className='flex items-center gap-1 bg-actionHover/50 border border-[var(--mui-palette-divider)] px-1.5 py-0.5 rounded text-[11px] text-textPrimary'>
                <Avatar sx={{ width: 18, height: 18, fontSize: 9, bgcolor: 'primary.main', fontWeight: 600 }}>
                  {getInitials(assigneeName)}
                </Avatar>
                <span className='truncate max-w-[80px] font-medium text-[10px]'>{assigneeName.split(' ')[0]}</span>
              </div>
            </Tooltip>

            {/* Team Badge */}
            {story.teamId && teamMap[story.teamId] && (
              <Tooltip title={`Team: ${teamMap[story.teamId]}`}>
                <span className='inline-flex items-center gap-1 rounded bg-secondary-lighter/20 text-secondary-dark font-medium border border-secondary-light/30 px-1.5 py-0.5 text-[10px]'>
                  👥 {teamMap[story.teamId]}
                </span>
              </Tooltip>
            )}

            {/* Move to sprint dropdown */}
            <CustomTextField
              select
              size='small'
              value={story.sprintId ? String(story.sprintId) : 'backlog'}
              onChange={e => handleMoveStorySprint(story, e.target.value === 'backlog' ? null : e.target.value)}
              className='w-32 text-xs'
              aria-label='Assign Sprint'
            >
              <MenuItem value='backlog' className='text-xs font-medium'>
                📋 Backlog
              </MenuItem>
              {sprints.map(s => (
                <MenuItem key={s.id} value={String(s.id)} className='text-xs'>
                  {String(s.status).toLowerCase().includes('active') ? '▶ ' : '🏃 '}
                  {s.name}
                </MenuItem>
              ))}
            </CustomTextField>

            <IconButton
              size='small'
              onClick={() => setStoryModal({ open: true, isEdit: true, data: { ...emptyStoryForm, ...story } })}
              aria-label='Edit story'
            >
              <i className='tabler-edit text-sm text-textSecondary' />
            </IconButton>

            <IconButton
              size='small'
              color='error'
              onClick={() => deleteStoryMutation.mutate(story.id)}
              aria-label='Delete story'
            >
              <i className='tabler-trash text-sm' />
            </IconButton>
          </div>
        </div>

        {/* Description preview */}
        {story.description && (
          <Typography variant='body2' className='text-xs text-textSecondary ml-9 mt-1 line-clamp-1'>
            {story.description}
          </Typography>
        )}

        {/* Collapsible Child Tasks */}
        <Collapse in={isExpanded} timeout='auto' unmountOnExit>
          <div className='mt-3 ml-8 pl-3 border-l-2 border-primary-main/40 flex flex-col gap-2'>
            <div className='flex items-center justify-between'>
              <Typography variant='caption' className='font-bold uppercase tracking-wider text-textSecondary text-[11px]'>
                Technical Sub-tasks ({childTasks.length})
              </Typography>
              <Button
                size='small'
                variant='text'
                className='text-xs font-semibold text-primary-main'
                startIcon={<i className='tabler-plus text-xs' />}
                onClick={() => setTaskModal({ open: true, isEdit: false, storyId: story.id, data: { ...emptyTaskForm, userStoryId: story.id } })}
              >
                Add Sub-task
              </Button>
            </div>

            {childTasks.length === 0 ? (
              <Typography variant='caption' className='italic text-textDisabled'>
                No sub-tasks added for this story yet. Click &quot;Add Sub-task&quot; to break down work.
              </Typography>
            ) : (
              childTasks.map(task => {
                const isDone = String(task.status).toLowerCase().includes('done')

                return (
                  <div
                    key={task.id}
                    className={`flex items-center justify-between p-2.5 rounded-lg border text-xs transition-colors ${
                      isDone ? 'bg-success-lighter/10 border-success-light/30' : 'bg-actionHover/30 border-[var(--mui-palette-divider)] hover:bg-actionHover/60'
                    }`}
                  >
                    <div className='flex items-center gap-2'>
                      <IconButton
                        size='small'
                        className='p-0.5'
                        onClick={() =>
                          updateTaskMutation.mutate({
                            id: task.id,
                            data: { ...task, status: isDone ? 'ToDo' : 'Done', projectId }
                          })
                        }
                        aria-label={isDone ? 'Reopen task' : 'Mark task complete'}
                      >
                        {isDone ? (
                          <i className='tabler-circle-check-filled text-success-main text-base' />
                        ) : (
                          <i className='tabler-circle text-textDisabled hover:text-success-main text-base' />
                        )}
                      </IconButton>
                      <span className='font-mono font-bold text-textSecondary'>TASK-{task.id}</span>
                      <span className={`font-medium ${isDone ? 'line-through text-textDisabled' : 'text-textPrimary'}`}>{task.title}</span>
                    </div>
                    <div className='flex items-center gap-2'>
                      {task.estimatedHours != null && (
                        <span className='text-textSecondary font-mono bg-backgroundPaper border border-[var(--mui-palette-divider)] px-1.5 py-0.5 rounded text-[11px]'>
                          ⏱ {task.estimatedHours}h
                        </span>
                      )}
                      <StatusChip value={task.status} size='small' />
                      {task.teamId && teamMap[task.teamId] && (
                        <span className='text-[10px] text-secondary-dark bg-secondary-lighter/20 border border-secondary-light/30 px-1.5 py-0.5 rounded font-medium'>
                          👥 {teamMap[task.teamId]}
                        </span>
                      )}
                      <span className='text-textPrimary font-medium text-[11px]'>
                        {userMap[task.assignedToUserId || task.assigneeUserId] || 'Unassigned'}
                      </span>
                      <IconButton
                        size='small'
                        className='p-1'
                        onClick={() => setTaskModal({ open: true, isEdit: true, storyId: story.id, data: { ...emptyTaskForm, ...task, userStoryId: story.id } })}
                        aria-label='Edit sub-task'
                      >
                        <i className='tabler-edit text-xs text-textSecondary' />
                      </IconButton>
                    </div>
                  </div>
                )
              })
            )}
          </div>
        </Collapse>
      </div>
    )
  }

  const totalBacklogPoints = backlogStories.reduce((sum, s) => sum + Number(s.storyPoints || 0), 0)

  return (
    <div className='flex flex-col gap-6'>
      {/* Top Header & Controls */}
      <div className='flex flex-wrap items-center justify-between gap-4 bg-backgroundPaper p-4 rounded-xl border border-[var(--mui-palette-divider)] shadow-sm'>
        <div className='flex items-center gap-3 flex-1 min-w-[280px]'>
          <CustomTextField
            size='small'
            placeholder='Filter stories by title or description…'
            value={searchQuery}
            onChange={e => setSearchQuery(e.target.value)}
            className='w-72'
            InputProps={{
              startAdornment: <i className='tabler-search text-textSecondary mr-2 text-sm' />
            }}
          />
          <CustomTextField
            select
            size='small'
            value={assigneeFilter}
            onChange={e => setAssigneeFilter(e.target.value)}
            className='w-44'
          >
            <MenuItem value='ALL'>All Assignees</MenuItem>
            {usersList.map(u => (
              <MenuItem key={u.id} value={String(u.id)}>
                {u.displayName || u.email}
              </MenuItem>
            ))}
          </CustomTextField>
          <CustomTextField
            select
            size='small'
            value={teamFilter}
            onChange={e => setTeamFilter(e.target.value)}
            className='w-44'
          >
            <MenuItem value='ALL'>All Teams</MenuItem>
            {teamsList.map(t => (
              <MenuItem key={t.id} value={String(t.id)}>
                👥 {t.name}
              </MenuItem>
            ))}
          </CustomTextField>
        </div>

        <div className='flex items-center gap-3'>
          <Button
            variant='outlined'
            size='small'
            className='font-semibold'
            startIcon={<i className='tabler-clock-plus' />}
            onClick={() => setSprintModal({ open: true, isEdit: false, data: emptySprintForm })}
          >
            Create Sprint
          </Button>
          <Button
            variant='contained'
            size='small'
            className='font-semibold shadow-sm'
            startIcon={<i className='tabler-plus' />}
            onClick={() => setStoryModal({ open: true, isEdit: false, data: emptyStoryForm })}
          >
            Create User Story
          </Button>
        </div>
      </div>

      {/* Active Sprints */}
      {activeSprints.map(sprint => renderSprintBox(sprint, 'active'))}

      {/* Planned Sprints */}
      {plannedSprints.map(sprint => renderSprintBox(sprint, 'planned'))}

      {/* Product Backlog Pool */}
      <Card className='border border-[var(--mui-palette-divider)] bg-backgroundPaper shadow-sm overflow-hidden'>
        <div className='flex flex-wrap items-center justify-between gap-3 p-4 bg-actionHover/40 border-b border-[var(--mui-palette-divider)]'>
          <div className='flex items-center gap-2.5'>
            <div className='w-8 h-8 rounded-lg bg-actionHover text-textSecondary flex items-center justify-center font-bold'>
              <i className='tabler-list-details text-base' />
            </div>
            <Typography variant='subtitle1' className='font-bold text-textPrimary'>
              Product Backlog Pool
            </Typography>
            <span className='inline-flex items-center px-2 py-0.5 rounded-full text-xs font-semibold bg-actionHover text-textSecondary border border-[var(--mui-palette-divider)]'>
              {backlogStories.length} Stories
            </span>
            <span className='inline-flex items-center gap-1 rounded-md bg-purple-500/10 text-purple-600 border border-purple-500/20 px-2 py-0.5 text-xs font-semibold'>
              <i className='tabler-calculator text-xs' />
              {totalBacklogPoints} Story Points
            </span>
          </div>
          <Button
            size='small'
            variant='tonal'
            startIcon={<i className='tabler-plus' />}
            onClick={() => setStoryModal({ open: true, isEdit: false, data: emptyStoryForm })}
          >
            Add to Backlog
          </Button>
        </div>

        <CardContent className='flex flex-col gap-2.5 p-4 bg-backgroundPaper'>
          {backlogStories.length === 0 ? (
            <div className='py-10 text-center border-2 border-dashed border-[var(--mui-palette-divider)] rounded-lg bg-actionHover/20'>
              <i className='tabler-folder-open text-4xl text-textDisabled mb-2' />
              <Typography variant='body2' className='text-textSecondary font-medium'>
                Your product backlog is empty.
              </Typography>
              <Typography variant='caption' className='text-textDisabled block mb-3'>
                Click &quot;Create User Story&quot; to plan requirements and features.
              </Typography>
              <Button
                size='small'
                variant='contained'
                startIcon={<i className='tabler-plus' />}
                onClick={() => setStoryModal({ open: true, isEdit: false, data: emptyStoryForm })}
              >
                Create First Story
              </Button>
            </div>
          ) : (
            backlogStories.map(story => renderStoryCard(story))
          )}
        </CardContent>
      </Card>

      {/* Completed Sprints (if any) */}
      {completedSprints.length > 0 && (
        <div className='flex flex-col gap-2 mt-4'>
          <Typography variant='subtitle2' className='font-bold uppercase tracking-wider text-textSecondary text-xs'>
            Completed Sprints ({completedSprints.length})
          </Typography>
          {completedSprints.map(sprint => (
            <Card key={sprint.id} className='bg-actionHover/30 border border-[var(--mui-palette-divider)] shadow-xs'>
              <CardContent className='flex items-center justify-between p-3.5'>
                <div className='flex items-center gap-3'>
                  <i className='tabler-circle-check text-success-main text-lg' />
                  <Typography className='font-bold text-textPrimary'>{sprint.name}</Typography>
                  <StatusChip value='Completed' size='small' />
                </div>
                <Typography variant='body2' className='text-textSecondary font-medium text-xs'>
                  {sprint.stories?.length || 0} stories finished
                </Typography>
              </CardContent>
            </Card>
          ))}
        </div>
      )}

      {/* CREATE / EDIT SPRINT MODAL */}
      <Dialog open={sprintModal.open} onClose={() => setSprintModal({ open: false, isEdit: false, data: emptySprintForm })} fullWidth maxWidth='sm'>
        <DialogTitle>{sprintModal.isEdit ? 'Edit Sprint' : 'Create New Sprint'}</DialogTitle>
        <DialogContent className='flex flex-col gap-4 pt-2'>
          <CustomTextField
            label='Sprint Name'
            placeholder='e.g., Sprint 1 — User Authentication'
            fullWidth
            required
            value={sprintModal.data.name}
            onChange={e => setSprintModal({ ...sprintModal, data: { ...sprintModal.data, name: e.target.value } })}
          />
          <CustomTextField
            label='Sprint Goal'
            placeholder='What is the primary objective of this sprint?'
            fullWidth
            multiline
            rows={2}
            value={sprintModal.data.goal}
            onChange={e => setSprintModal({ ...sprintModal, data: { ...sprintModal.data, goal: e.target.value } })}
          />
          <div className='grid grid-cols-2 gap-4'>
            <CustomTextField
              label='Start Date'
              type='date'
              fullWidth
              InputLabelProps={{ shrink: true }}
              value={sprintModal.data.startDate}
              onChange={e => setSprintModal({ ...sprintModal, data: { ...sprintModal.data, startDate: e.target.value } })}
            />
            <CustomTextField
              label='End Date'
              type='date'
              fullWidth
              InputLabelProps={{ shrink: true }}
              value={sprintModal.data.endDate}
              onChange={e => setSprintModal({ ...sprintModal, data: { ...sprintModal.data, endDate: e.target.value } })}
            />
          </div>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setSprintModal({ open: false, isEdit: false, data: emptySprintForm })}>Cancel</Button>
          <Button
            variant='contained'
            disabled={!sprintModal.data.name || createMutation.isPending}
            onClick={handleCreateSprint}
          >
            Create Sprint
          </Button>
        </DialogActions>
      </Dialog>

      {/* CREATE / EDIT USER STORY MODAL */}
      <Dialog open={storyModal.open} onClose={() => setStoryModal({ open: false, isEdit: false, data: emptyStoryForm })} fullWidth maxWidth='md'>
        <DialogTitle>{storyModal.isEdit ? 'Edit User Story' : 'Create User Story'}</DialogTitle>
        <DialogContent className='flex flex-col gap-4 pt-2'>
          <CustomTextField
            label='Story Title / Requirement'
            placeholder='e.g., As a user, I want to reset my password via email'
            fullWidth
            required
            value={storyModal.data.title}
            onChange={e => setStoryModal({ ...storyModal, data: { ...storyModal.data, title: e.target.value } })}
          />
          <CustomTextField
            label='Description'
            placeholder='Provide context, user personas, or business reasons…'
            fullWidth
            multiline
            rows={3}
            value={storyModal.data.description}
            onChange={e => setStoryModal({ ...storyModal, data: { ...storyModal.data, description: e.target.value } })}
          />
          <CustomTextField
            label='Acceptance Criteria'
            placeholder='e.g., Given a registered user, when they click forgot password, then an email with a 1-hour token is sent.'
            fullWidth
            multiline
            rows={2}
            value={storyModal.data.acceptanceCriteria}
            onChange={e => setStoryModal({ ...storyModal, data: { ...storyModal.data, acceptanceCriteria: e.target.value } })}
          />
          <div className='grid grid-cols-1 md:grid-cols-4 gap-4'>
            <CustomTextField
              label='Story Points'
              type='number'
              fullWidth
              value={storyModal.data.storyPoints}
              onChange={e => setStoryModal({ ...storyModal, data: { ...storyModal.data, storyPoints: Number(e.target.value) } })}
            />
            <CustomTextField
              select
              label='Priority'
              fullWidth
              value={storyModal.data.priority}
              onChange={e => setStoryModal({ ...storyModal, data: { ...storyModal.data, priority: Number(e.target.value) } })}
            >
              <MenuItem value={1}>Highest</MenuItem>
              <MenuItem value={2}>High</MenuItem>
              <MenuItem value={3}>Medium</MenuItem>
              <MenuItem value={4}>Low</MenuItem>
              <MenuItem value={5}>Lowest</MenuItem>
            </CustomTextField>
            <CustomTextField
              select
              label='Assignee'
              fullWidth
              value={storyModal.data.assignedToUserId ? String(storyModal.data.assignedToUserId) : ''}
              onChange={e => setStoryModal({ ...storyModal, data: { ...storyModal.data, assignedToUserId: e.target.value ? Number(e.target.value) : null } })}
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
              value={storyModal.data.teamId ? String(storyModal.data.teamId) : ''}
              onChange={e => setStoryModal({ ...storyModal, data: { ...storyModal.data, teamId: e.target.value ? Number(e.target.value) : null } })}
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
            select
            label='Sprint Allocation'
            fullWidth
            value={storyModal.data.sprintId ? String(storyModal.data.sprintId) : ''}
            onChange={e => setStoryModal({ ...storyModal, data: { ...storyModal.data, sprintId: e.target.value ? Number(e.target.value) : null } })}
          >
            <MenuItem value=''>📋 Product Backlog (Unplanned)</MenuItem>
            {sprints.map(s => (
              <MenuItem key={s.id} value={String(s.id)}>
                {String(s.status).toLowerCase().includes('active') ? '▶ ' : '🏃 '}
                {s.name}
              </MenuItem>
            ))}
          </CustomTextField>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setStoryModal({ open: false, isEdit: false, data: emptyStoryForm })}>Cancel</Button>
          <Button
            variant='contained'
            disabled={!storyModal.data.title || createStoryMutation.isPending || updateStoryMutation.isPending}
            onClick={() => {
              if (storyModal.isEdit) {
                updateStoryMutation.mutate({ id: storyModal.data.id, data: { ...storyModal.data, projectId } })
                setStoryModal({ open: false, isEdit: false, data: emptyStoryForm })
              } else {
                createStoryMutation.mutate(storyModal.data)
              }
            }}
          >
            {storyModal.isEdit ? 'Save Changes' : 'Save Story'}
          </Button>
        </DialogActions>
      </Dialog>

      {/* CREATE / EDIT SUB-TASK MODAL */}
      <Dialog open={taskModal.open} onClose={() => setTaskModal({ open: false, isEdit: false, storyId: null, data: emptyTaskForm })} fullWidth maxWidth='sm'>
        <DialogTitle>{taskModal.isEdit ? `Edit Sub-task #TASK-${taskModal.data?.id}` : 'Add Technical Sub-task'}</DialogTitle>
        <DialogContent className='flex flex-col gap-4 pt-2'>
          <CustomTextField
            label='Task Title'
            placeholder='e.g., Create password reset API endpoint in .NET'
            fullWidth
            required
            value={taskModal.data.title}
            onChange={e => setTaskModal({ ...taskModal, data: { ...taskModal.data, title: e.target.value } })}
          />
          <CustomTextField
            label='Task Description / Implementation Details'
            placeholder='Technical steps or notes…'
            fullWidth
            multiline
            rows={2}
            value={taskModal.data.description}
            onChange={e => setTaskModal({ ...taskModal, data: { ...taskModal.data, description: e.target.value } })}
          />
          <div className='grid grid-cols-2 gap-4'>
            <CustomTextField
              label='Estimated Hours'
              type='number'
              fullWidth
              value={taskModal.data.estimatedHours}
              onChange={e => setTaskModal({ ...taskModal, data: { ...taskModal.data, estimatedHours: Number(e.target.value) } })}
            />
            <CustomTextField
              select
              label='Status'
              fullWidth
              value={taskModal.data.status || 'ToDo'}
              onChange={e => setTaskModal({ ...taskModal, data: { ...taskModal.data, status: e.target.value } })}
            >
              <MenuItem value='ToDo'>To Do</MenuItem>
              <MenuItem value='InProgress'>In Progress</MenuItem>
              <MenuItem value='Review'>Review</MenuItem>
              <MenuItem value='Done'>Done</MenuItem>
            </CustomTextField>
          </div>
          <div className='grid grid-cols-2 gap-4'>
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
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setTaskModal({ open: false, isEdit: false, storyId: null, data: emptyTaskForm })}>Cancel</Button>
          <Button
            variant='contained'
            disabled={!taskModal.data.title || createTaskMutation.isPending || updateTaskMutation.isPending}
            onClick={() => {
              if (taskModal.isEdit) {
                updateTaskMutation.mutate({ id: taskModal.data.id, data: { ...taskModal.data, projectId } })
                setTaskModal({ open: false, isEdit: false, storyId: null, data: emptyTaskForm })
              } else {
                createTaskMutation.mutate(taskModal.data)
              }
            }}
          >
            {taskModal.isEdit ? 'Save Task' : 'Create Task'}
          </Button>
        </DialogActions>
      </Dialog>

      {/* CONFIRM SPRINT ACTION MODAL */}
      <Dialog open={confirmSprint.open} onClose={() => setConfirmSprint({ open: false, type: '', sprint: null })}>
        <DialogTitle>
          {confirmSprint.type === 'start' ? `Start Sprint: ${confirmSprint.sprint?.name}?` : `Complete Sprint: ${confirmSprint.sprint?.name}?`}
        </DialogTitle>
        <DialogContent>
          <Typography color='text.secondary'>
            {confirmSprint.type === 'start'
              ? 'Starting this sprint will activate its stories on the Active Board.'
              : 'Completing this sprint will close out finished stories and calculate final sprint velocity.'}
          </Typography>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setConfirmSprint({ open: false, type: '', sprint: null })}>Cancel</Button>
          <Button
            variant='contained'
            color={confirmSprint.type === 'start' ? 'primary' : 'success'}
            onClick={() => (confirmSprint.type === 'start' ? handleStartSprintAction(confirmSprint.sprint) : handleCompleteSprintAction(confirmSprint.sprint))}
          >
            {confirmSprint.type === 'start' ? 'Start Sprint' : 'Complete Sprint'}
          </Button>
        </DialogActions>
      </Dialog>
    </div>
  )
}

export default ProjectBacklogView
