'use client'

import { useState, useMemo } from 'react'

import Card from '@mui/material/Card'
import CardContent from '@mui/material/CardContent'
import Typography from '@mui/material/Typography'
import Button from '@mui/material/Button'
import IconButton from '@mui/material/IconButton'
import Chip from '@mui/material/Chip'
import Collapse from '@mui/material/Collapse'
import Tooltip from '@mui/material/Tooltip'
import Avatar from '@mui/material/Avatar'
import LinearProgress from '@mui/material/LinearProgress'
import MenuItem from '@mui/material/MenuItem'
import Dialog from '@mui/material/Dialog'
import DialogTitle from '@mui/material/DialogTitle'
import DialogContent from '@mui/material/DialogContent'
import DialogActions from '@mui/material/DialogActions'

import { toast } from 'react-toastify'
import { useMutation, useQueryClient } from '@tanstack/react-query'

import CustomTextField from '@core/components/mui/TextField'

import StatusChip from '@/components/pmt/StatusChip'
import { useLookups } from '@/hooks/useLookups'
import { tasksService } from '@/services/tasks'
import { storiesService } from '@/services/stories'
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

const emptyStoryForm = {
  title: '',
  description: '',
  acceptanceCriteria: '',
  priority: 3,
  storyPoints: 3,
  sprintId: null,
  assignedToUserId: null,
  teamId: null
}

export const ProjectHierarchyView = ({ projectId, projectKey, stories = [], tasks = [], onRefresh }) => {
  const queryClient = useQueryClient()
  const { data: lookups = {} } = useLookups()

  const [expandedStories, setExpandedStories] = useState({})
  const [taskModal, setTaskModal] = useState({ open: false, isEdit: false, data: emptyTaskForm })
  const [storyModal, setStoryModal] = useState({ open: false, isEdit: false, data: emptyStoryForm })
  const [searchQuery, setSearchQuery] = useState('')

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

  const invalidateAll = () => {
    queryClient.invalidateQueries({ queryKey: ['project-detail', projectId] })
    if (onRefresh) onRefresh()
  }

  // Update Task Mutation
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
      toast.success('Task added')
      setTaskModal({ open: false, isEdit: false, data: emptyTaskForm })
      invalidateAll()
    },
    onError: error => toast.error(extractErrors(error.response?.data)[0] || 'Failed to create task')
  })

  // Create Story Mutation
  const createStoryMutation = useMutation({
    mutationFn: data => storiesService.create({ ...data, projectId }),
    onSuccess: () => {
      toast.success('User Story created')
      setStoryModal({ open: false, isEdit: false, data: emptyStoryForm })
      invalidateAll()
    },
    onError: error => toast.error(extractErrors(error.response?.data)[0] || 'Failed to create story')
  })

  // Update Story Mutation
  const updateStoryMutation = useMutation({
    mutationFn: ({ id, data }) => storiesService.update(id, data),
    onSuccess: () => {
      toast.success('User Story updated')
      setStoryModal({ open: false, isEdit: false, data: emptyStoryForm })
      invalidateAll()
    },
    onError: error => toast.error(extractErrors(error.response?.data)[0] || 'Failed to update story')
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

  // Group tasks by Story
  const { storyTasksMap, orphanTasks } = useMemo(() => {
    const map = {}
    const orphans = []

    tasks.forEach(t => {
      const parentId = t.userStoryId || t.storyId

      if (parentId) {
        if (!map[parentId]) map[parentId] = []
        map[parentId].push(t)
      } else {
        orphans.push(t)
      }
    })

    return { storyTasksMap: map, orphanTasks: orphans }
  }, [tasks])

  const toggleExpand = storyId => {
    setExpandedStories(prev => ({ ...prev, [storyId]: !prev[storyId] }))
  }

  const expandAll = () => {
    const map = {}

    stories.forEach(s => {
      map[s.id] = true
    })
    setExpandedStories(map)
  }

  const collapseAll = () => {
    setExpandedStories({})
  }

  // Filtered stories
  const filteredStories = useMemo(() => {
    if (!searchQuery) return stories
    const q = searchQuery.toLowerCase()

    
return stories.filter(s => s.title?.toLowerCase().includes(q) || s.description?.toLowerCase().includes(q))
  }, [stories, searchQuery])

  // Total project roll-up metrics
  const totalEstimatedHours = tasks.reduce((sum, t) => sum + Number(t.estimatedHours || 0), 0)
  const totalActualHours = tasks.reduce((sum, t) => sum + Number(t.actualHours || 0), 0)
  const totalStoryPoints = stories.reduce((sum, s) => sum + Number(s.storyPoints || 0), 0)
  const completedTasksTotal = tasks.filter(t => String(t.status).toLowerCase().includes('done')).length
  const totalProgressPercent = tasks.length ? Math.round((completedTasksTotal / tasks.length) * 100) : 0

  return (
    <div className='flex flex-col gap-6'>
      {/* Metrics Summary Strip */}
      <div className='grid grid-cols-1 sm:grid-cols-2 md:grid-cols-4 gap-4'>
        <Card className='border border-slate-200 bg-white shadow-xs'>
          <CardContent className='p-4 flex items-center gap-3'>
            <div className='p-3 bg-purple-50 text-purple-700 border border-purple-200 rounded-xl'>
              <i className='tabler-bulb text-2xl' />
            </div>
            <div>
              <Typography variant='caption' className='text-slate-500 font-bold uppercase tracking-wider text-[10px]'>
                User Stories
              </Typography>
              <Typography variant='h5' className='font-bold text-slate-900'>
                {stories.length} <span className='text-xs font-normal text-slate-500'>({totalStoryPoints} pts)</span>
              </Typography>
            </div>
          </CardContent>
        </Card>

        <Card className='border border-slate-200 bg-white shadow-xs'>
          <CardContent className='p-4 flex items-center gap-3'>
            <div className='p-3 bg-blue-50 text-blue-700 border border-blue-200 rounded-xl'>
              <i className='tabler-checklist text-2xl' />
            </div>
            <div>
              <Typography variant='caption' className='text-slate-500 font-bold uppercase tracking-wider text-[10px]'>
                Total Tasks
              </Typography>
              <Typography variant='h5' className='font-bold text-slate-900'>
                {tasks.length} <span className='text-xs font-normal text-green-700 font-semibold'>({completedTasksTotal} done)</span>
              </Typography>
            </div>
          </CardContent>
        </Card>

        <Card className='border border-slate-200 bg-white shadow-xs'>
          <CardContent className='p-4 flex items-center gap-3'>
            <div className='p-3 bg-amber-50 text-amber-700 border border-amber-200 rounded-xl'>
              <i className='tabler-clock-hour-4 text-2xl' />
            </div>
            <div>
              <Typography variant='caption' className='text-slate-500 font-bold uppercase tracking-wider text-[10px]'>
                Estimated Effort
              </Typography>
              <Typography variant='h5' className='font-bold text-slate-900'>
                {totalEstimatedHours}h <span className='text-xs font-normal text-slate-500'>({totalActualHours}h spent)</span>
              </Typography>
            </div>
          </CardContent>
        </Card>

        <Card className='border border-slate-200 bg-white shadow-xs'>
          <CardContent className='p-4 flex flex-col justify-center gap-1'>
            <div className='flex items-center justify-between'>
              <Typography variant='caption' className='text-slate-500 font-bold uppercase tracking-wider text-[10px]'>
                Overall Completion
              </Typography>
              <Typography variant='body2' className='font-bold text-primary-main'>
                {totalProgressPercent}%
              </Typography>
            </div>
            <LinearProgress
              variant='determinate'
              value={totalProgressPercent}
              className='h-2 rounded-full bg-slate-200'
              color={totalProgressPercent === 100 ? 'success' : 'primary'}
            />
          </CardContent>
        </Card>
      </div>

      {/* Header & Controls */}
      <div className='flex flex-wrap items-center justify-between gap-4 bg-white p-4 rounded-xl border border-slate-200 shadow-xs'>
        <div className='flex items-center gap-3 flex-1 min-w-[280px]'>
          <CustomTextField
            size='small'
            placeholder='Search stories & tasks…'
            value={searchQuery}
            onChange={e => setSearchQuery(e.target.value)}
            className='w-72 bg-white'
            InputProps={{
              startAdornment: <i className='tabler-search text-slate-400 mr-2' />
            }}
          />
          <Button size='small' variant='tonal' onClick={expandAll} startIcon={<i className='tabler-arrows-maximize' />}>
            Expand All
          </Button>
          <Button size='small' variant='tonal' onClick={collapseAll} startIcon={<i className='tabler-arrows-minimize' />}>
            Collapse All
          </Button>
        </div>

        <div className='flex items-center gap-3'>
          <Button
            variant='outlined'
            className='bg-white font-semibold'
            startIcon={<i className='tabler-plus' />}
            onClick={() => setTaskModal({ open: true, isEdit: false, data: emptyTaskForm })}
          >
            Add Standalone Task
          </Button>
          <Button
            variant='contained'
            className='font-semibold shadow-sm'
            startIcon={<i className='tabler-plus' />}
            onClick={() => setStoryModal({ open: true, isEdit: false, data: emptyStoryForm })}
          >
            Create User Story
          </Button>
        </div>
      </div>

      {/* Hierarchy Stories Tree */}
      <div className='flex flex-col gap-4'>
        {filteredStories.length === 0 ? (
          <Card className='border border-slate-200 bg-white p-10 text-center shadow-sm'>
            <i className='tabler-folder-open text-4xl text-slate-400 mb-2' />
            <Typography variant='h6' className='text-slate-700 font-bold mb-1'>
              No User Stories recorded yet
            </Typography>
            <Typography variant='body2' className='text-slate-500 mb-4'>
              Click &quot;Create User Story&quot; above to begin defining requirements and tasks.
            </Typography>
            <Button
              variant='contained'
              startIcon={<i className='tabler-plus' />}
              onClick={() => setStoryModal({ open: true, isEdit: false, data: emptyStoryForm })}
            >
              Create First Story
            </Button>
          </Card>
        ) : (
          filteredStories.map(story => {
            const childTasks = storyTasksMap[story.id] || []
            const isExpanded = expandedStories[story.id] ?? true // default expanded
            const assigneeName = userMap[story.assignedToUserId || story.assigneeUserId] || 'Unassigned'
            const storyEstHours = childTasks.reduce((sum, t) => sum + Number(t.estimatedHours || 0), 0)
            const storyActHours = childTasks.reduce((sum, t) => sum + Number(t.actualHours || 0), 0)
            const doneTasks = childTasks.filter(t => String(t.status).toLowerCase().includes('done')).length
            const storyProgress = childTasks.length ? Math.round((doneTasks / childTasks.length) * 100) : 0

            return (
              <Card
                key={story.id}
                className='border border-slate-200 bg-white shadow-sm transition-all overflow-hidden'
              >
                {/* Story Parent Row */}
                <div
                  className='p-4 bg-slate-50/90 border-b border-slate-200 flex flex-wrap items-center justify-between gap-3 cursor-pointer hover:bg-slate-100/90 transition-colors'
                  onClick={() => toggleExpand(story.id)}
                >
                  <div className='flex items-center gap-3 flex-1 min-w-[280px]'>
                    <IconButton size='small' aria-label='Toggle child tasks' className='text-slate-600'>
                      <i className={isExpanded ? 'tabler-chevron-down text-lg' : 'tabler-chevron-right text-lg'} />
                    </IconButton>

                    <Chip
                      size='small'
                      label={`STORY-${story.id}`}
                      color='primary'
                      variant='tonal'
                      className='font-mono font-bold text-xs'
                    />

                    <div className='flex flex-col'>
                      <Typography className='font-bold text-base text-slate-900'>
                        {story.title}
                      </Typography>
                      {story.description && (
                        <Typography variant='caption' className='text-slate-500 line-clamp-1'>
                          {story.description}
                        </Typography>
                      )}
                    </div>
                  </div>

                  <div className='flex items-center gap-2.5 flex-wrap' onClick={e => e.stopPropagation()}>
                    {/* Story Points */}
                    <Tooltip title='Story Points'>
                      <Chip
                        size='small'
                        label={`${story.storyPoints ?? 0} pts`}
                        className='bg-purple-50 text-purple-700 font-bold border border-purple-200 text-xs'
                      />
                    </Tooltip>

                    {/* Roll-up Hours */}
                    <span className='text-xs font-mono text-slate-600 bg-white border border-slate-200 px-2 py-1 rounded-md font-medium'>
                      ⏱ {storyEstHours}h est · {storyActHours}h spent
                    </span>

                    {/* Priority */}
                    <Chip
                      size='small'
                      label={priorityLabel(story.priority)}
                      color={priorityColor(story.priority)}
                      variant='tonal'
                    />

                    {/* Status */}
                    <StatusChip value={story.status} />

                    {/* Assignee */}
                    <div className='flex items-center gap-1.5 text-xs text-slate-800 bg-white px-2.5 py-1 rounded-md border border-slate-200'>
                      <Avatar sx={{ width: 18, height: 18, fontSize: 10, bgcolor: '#6366f1' }}>{assigneeName[0]}</Avatar>
                      <span className='font-medium'>{assigneeName}</span>
                    </div>

                    {/* Team Badge */}
                    {story.teamId && teamMap[story.teamId] && (
                      <Chip
                        size='small'
                        label={`👥 ${teamMap[story.teamId]}`}
                        className='bg-blue-50 text-blue-700 font-medium border border-blue-200 text-xs'
                      />
                    )}

                    <IconButton
                      size='small'
                      onClick={() => setStoryModal({ open: true, isEdit: true, data: { ...emptyStoryForm, ...story } })}
                      aria-label='Edit story'
                      className='text-slate-600 hover:text-primary-main'
                    >
                      <i className='tabler-edit text-sm' />
                    </IconButton>

                    <Button
                      size='small'
                      variant='tonal'
                      startIcon={<i className='tabler-plus text-xs' />}
                      onClick={() => setTaskModal({ open: true, isEdit: false, data: { ...emptyTaskForm, userStoryId: story.id } })}
                    >
                      Add Task
                    </Button>
                  </div>
                </div>

                {/* Progress Bar under Story */}
                {childTasks.length > 0 && (
                  <div className='px-4 py-2 bg-slate-50/50 border-b border-slate-200/80 flex items-center gap-3'>
                    <Typography variant='caption' className='text-xs font-bold text-slate-600'>
                      Tasks Completed: {doneTasks}/{childTasks.length} ({storyProgress}%)
                    </Typography>
                    <LinearProgress
                      variant='determinate'
                      value={storyProgress}
                      className='flex-1 h-1.5 rounded-full bg-slate-200'
                      color={storyProgress === 100 ? 'success' : 'primary'}
                    />
                  </div>
                )}

                {/* Collapsible Child Tasks List */}
                <Collapse in={isExpanded} timeout='auto' unmountOnExit>
                  <div className='p-4 bg-white flex flex-col gap-2.5'>
                    {childTasks.length === 0 ? (
                      <div className='py-5 text-center border border-dashed border-slate-200 rounded-lg bg-slate-50/50'>
                        <Typography variant='caption' className='text-slate-500 font-medium'>
                          No sub-tasks defined for this User Story yet. Click &quot;Add Task&quot; to break down work.
                        </Typography>
                      </div>
                    ) : (
                      childTasks.map(task => {
                        const taskAssignee = userMap[task.assignedToUserId || task.assigneeUserId] || 'Unassigned'
                        const isOverdue = task.dueDate && new Date(task.dueDate) < new Date() && !String(task.status).toLowerCase().includes('done')
                        const isDone = String(task.status).toLowerCase().includes('done')

                        return (
                          <div
                            key={task.id}
                            className={`flex flex-wrap items-center justify-between gap-3 p-3 rounded-lg border transition-colors ${
                              isDone ? 'bg-success-lighter/10 border-success-light/30' : 'border-slate-200 bg-slate-50/60 hover:bg-slate-100/70'
                            }`}
                          >
                            <div className='flex items-center gap-2.5 flex-1 min-w-[240px]'>
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
                                  <i className='tabler-circle text-slate-400 hover:text-success-main text-base' />
                                )}
                              </IconButton>
                              <span className='font-mono font-bold text-xs text-slate-500'>TASK-{task.id}</span>
                              <Typography className={`font-medium text-sm ${isDone ? 'line-through text-slate-400' : 'text-slate-900'}`}>
                                {task.title}
                              </Typography>
                              {task.dueDate && (
                                <span className={`text-[11px] font-mono px-1.5 py-0.5 rounded border ${isOverdue ? 'bg-red-50 text-red-700 font-bold border-red-200' : 'text-slate-600 bg-white border-slate-200'}`}>
                                  📅 {task.dueDate.substring(0, 10)} {isOverdue && '⚠️ OVERDUE'}
                                </span>
                              )}
                            </div>

                            <div className='flex items-center gap-2.5'>
                              <span className='text-xs font-mono text-slate-600 bg-white border border-slate-200 px-2 py-1 rounded'>
                                ⏱ {task.estimatedHours ?? 0}h est {task.actualHours != null && `· ${task.actualHours}h spent`}
                              </span>

                              {/* Status Chip */}
                              <StatusChip value={task.status} />

                              {/* Team Badge */}
                              {task.teamId && teamMap[task.teamId] && (
                                <span className='text-xs text-blue-700 bg-blue-50 border border-blue-200 px-2 py-0.5 rounded font-medium'>
                                  👥 {teamMap[task.teamId]}
                                </span>
                              )}

                              {/* Assignee */}
                              <div className='flex items-center gap-1.5 bg-white px-2 py-1 rounded-md border border-slate-200 text-xs text-slate-800'>
                                <Avatar sx={{ width: 18, height: 18, fontSize: 9, bgcolor: '#6366f1' }}>{taskAssignee[0]}</Avatar>
                                <span className='truncate max-w-[90px] font-medium'>{taskAssignee}</span>
                              </div>

                              <IconButton
                                size='small'
                                onClick={() => setTaskModal({ open: true, isEdit: true, data: { ...emptyTaskForm, ...task, userStoryId: task.userStoryId || task.storyId } })}
                                aria-label='Edit task'
                              >
                                <i className='tabler-edit text-sm text-slate-600' />
                              </IconButton>

                              <IconButton
                                size='small'
                                color='error'
                                onClick={() => deleteTaskMutation.mutate(task.id)}
                                aria-label='Delete task'
                              >
                                <i className='tabler-trash text-sm' />
                              </IconButton>
                            </div>
                          </div>
                        )
                      })
                    )}
                  </div>
                </Collapse>
              </Card>
            )
          })
        )}

        {/* Orphan Tasks Section */}
        {orphanTasks.length > 0 && (
          <Card className='border border-dashed border-amber-300 bg-amber-50/40 mt-4 shadow-xs'>
            <CardContent className='flex flex-col gap-3 p-4'>
              <div className='flex items-center justify-between'>
                <div className='flex items-center gap-2'>
                  <i className='tabler-alert-circle text-amber-600 text-lg' />
                  <Typography variant='subtitle1' className='font-bold text-amber-900'>
                    Standalone Tasks without User Story ({orphanTasks.length})
                  </Typography>
                </div>
                <Typography variant='caption' className='text-amber-700 font-medium'>
                  Tip: Edit these tasks to link them to a parent feature story.
                </Typography>
              </div>

              <div className='flex flex-col gap-2'>
                {orphanTasks.map(task => {
                  const taskAssignee = userMap[task.assignedToUserId || task.assigneeUserId] || 'Unassigned'

                  return (
                    <div
                      key={task.id}
                      className='flex items-center justify-between p-2.5 rounded-lg bg-white border border-amber-200 text-xs shadow-2xs'
                    >
                      <div className='flex items-center gap-2'>
                        <span className='font-mono font-bold text-slate-600'>TASK-{task.id}</span>
                        <span className='font-medium text-slate-900'>{task.title}</span>
                      </div>
                      <div className='flex items-center gap-2'>
                        <StatusChip value={task.status} />
                        <span className='text-slate-600 font-medium'>{taskAssignee}</span>
                        <Button
                          size='small'
                          variant='tonal'
                          onClick={() => setTaskModal({ open: true, isEdit: true, data: task })}
                        >
                          Link to Story
                        </Button>
                      </div>
                    </div>
                  )
                })}
              </div>
            </CardContent>
          </Card>
        )}
      </div>

      {/* CREATE / EDIT TASK MODAL */}
      <Dialog open={taskModal.open} onClose={() => setTaskModal({ open: false, isEdit: false, data: emptyTaskForm })} fullWidth maxWidth='sm'>
        <DialogTitle>{taskModal.isEdit ? `Edit Task #${taskModal.data?.id}` : 'Create Technical Task'}</DialogTitle>
        <DialogContent className='flex flex-col gap-4 pt-2'>
          <CustomTextField
            label='Task Title'
            placeholder='e.g., Setup database migrations for Sprints'
            fullWidth
            required
            value={taskModal.data.title}
            onChange={e => setTaskModal({ ...taskModal, data: { ...taskModal.data, title: e.target.value } })}
          />
          <CustomTextField
            select
            label='Parent User Story'
            fullWidth
            value={taskModal.data.userStoryId ? String(taskModal.data.userStoryId) : ''}
            onChange={e => setTaskModal({ ...taskModal, data: { ...taskModal.data, userStoryId: e.target.value ? Number(e.target.value) : null } })}
          >
            <MenuItem value=''>None (Standalone)</MenuItem>
            {stories.map(s => (
              <MenuItem key={s.id} value={String(s.id)}>
                STORY-{s.id}: {s.title}
              </MenuItem>
            ))}
          </CustomTextField>
          <CustomTextField
            label='Description / Technical Steps'
            placeholder='Technical requirements…'
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
              label='Actual Hours Spent'
              type='number'
              fullWidth
              value={taskModal.data.actualHours ?? ''}
              onChange={e => setTaskModal({ ...taskModal, data: { ...taskModal.data, actualHours: e.target.value ? Number(e.target.value) : null } })}
            />
          </div>
          <div className='grid grid-cols-2 gap-4'>
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
            {taskModal.isEdit ? 'Save Changes' : 'Create Task'}
          </Button>
        </DialogActions>
      </Dialog>

      {/* CREATE / EDIT USER STORY MODAL */}
      <Dialog open={storyModal.open} onClose={() => setStoryModal({ open: false, isEdit: false, data: emptyStoryForm })} fullWidth maxWidth='sm'>
        <DialogTitle>{storyModal.isEdit ? `Edit Story #STORY-${storyModal.data?.id}` : 'Create User Story'}</DialogTitle>
        <DialogContent className='flex flex-col gap-4 pt-2'>
          <CustomTextField
            label='Story Title / Requirement'
            placeholder='e.g., As an admin, I want to export sprint burndown charts'
            fullWidth
            required
            value={storyModal.data.title}
            onChange={e => setStoryModal({ ...storyModal, data: { ...storyModal.data, title: e.target.value } })}
          />
          <CustomTextField
            label='Description'
            placeholder='Details, personas, or context…'
            fullWidth
            multiline
            rows={2}
            value={storyModal.data.description}
            onChange={e => setStoryModal({ ...storyModal, data: { ...storyModal.data, description: e.target.value } })}
          />
          <div className='grid grid-cols-2 gap-4'>
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
          </div>
          <div className='grid grid-cols-2 gap-4'>
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
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setStoryModal({ open: false, isEdit: false, data: emptyStoryForm })}>Cancel</Button>
          <Button
            variant='contained'
            disabled={!storyModal.data.title || createStoryMutation.isPending || updateStoryMutation.isPending}
            onClick={() => {
              if (storyModal.isEdit) {
                updateStoryMutation.mutate({ id: storyModal.data.id, data: { ...storyModal.data, projectId } })
              } else {
                createStoryMutation.mutate(storyModal.data)
              }
            }}
          >
            {storyModal.isEdit ? 'Save Changes' : 'Create Story'}
          </Button>
        </DialogActions>
      </Dialog>
    </div>
  )
}

export default ProjectHierarchyView
