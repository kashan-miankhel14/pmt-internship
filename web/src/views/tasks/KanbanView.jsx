'use client'

// React Imports
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'

// Next Imports
import dynamic from 'next/dynamic'
import { useSearchParams } from 'next/navigation'

// MUI Imports
import Button from '@mui/material/Button'
import Card from '@mui/material/Card'
import CardContent from '@mui/material/CardContent'
import Typography from '@mui/material/Typography'
import FormControl from '@mui/material/FormControl'
import InputLabel from '@mui/material/InputLabel'
import Select from '@mui/material/Select'
import MenuItem from '@mui/material/MenuItem'
import CircularProgress from '@mui/material/CircularProgress'
import ToggleButton from '@mui/material/ToggleButton'
import ToggleButtonGroup from '@mui/material/ToggleButtonGroup'
import Tooltip from '@mui/material/Tooltip'

// Third-party Imports
import { toast } from 'react-toastify'

// Component Imports
import PageHeader from '@/components/pmt/PageHeader'
import EmptyState from '@/components/pmt/EmptyState'
import EntityDialog from '@/components/pmt/EntityDialog'
import ErrorState from '@/components/pmt/ErrorState'
import ConfirmDialog from '@/components/pmt/ConfirmDialog'
import CommentsPanel from '@/components/pmt/CommentsPanel'
import KanbanBoard from '@/components/pmt/KanbanBoard'
import BoardColumnManager from '@/components/pmt/BoardColumnManager'
import CustomTextField from '@core/components/mui/TextField'

// Config Imports
import themeConfig from '@configs/themeConfig'

// Hook Imports
import { useCrudModule } from '@/hooks/useCrudModule'
import { useLookups } from '@/hooks/useLookups'
import { useBoardColumns } from '@/hooks/useBoardColumns'

// Context Imports
import { Can, useAbility } from '@/contexts/AbilityContext'

// Lib Imports
import { moduleMeta } from '@/libs/moduleMeta'
import { extractErrors } from '@/libs/errors'
import { priorityLabel } from '@/libs/enums'

// Service Imports
import { tasksService } from '@/services/tasks'

const STATUSES = ['ToDo', 'InProgress', 'Blocked', 'Review', 'Done']

const ModuleTable = dynamic(() => import('@/components/pmt/ModuleTable'), {
  loading: () => (
    <div className='flex min-bs-[60vh] items-center justify-center'>
      <CircularProgress />
    </div>
  )
})

const EMPTY_LOOKUPS = {}

const mapTaskStatusToColumn = (taskStatus, columnStatuses) => {
  const normalizedStatuses = columnStatuses.map(s => String(s).toLowerCase())
  const hasBacklog = normalizedStatuses.includes('backlog')
  const hasToDo = normalizedStatuses.includes('todo')

  if (hasBacklog) {
    if (taskStatus === 'ToDo') {
      const idx = normalizedStatuses.indexOf('backlog')
      return columnStatuses[idx]
    }
  } else if (hasToDo) {
    if (taskStatus === 'ToDo') {
      const idx = normalizedStatuses.indexOf('todo')
      return columnStatuses[idx]
    }
  }

  const matchIdx = normalizedStatuses.indexOf(String(taskStatus).toLowerCase())
  if (matchIdx !== -1) {
    return columnStatuses[matchIdx]
  }

  return columnStatuses[0]
}

const mapColumnToTaskStatus = (colStatus, columnStatuses) => {
  const normalizedColStatus = String(colStatus).toLowerCase()
  if (normalizedColStatus === 'backlog' || normalizedColStatus === 'selected' || normalizedColStatus === 'todo') {
    return 'ToDo'
  }
  
  const standardStatuses = ['ToDo', 'InProgress', 'Blocked', 'Review', 'Done', 'Cancelled']
  const matched = standardStatuses.find(s => s.toLowerCase() === normalizedColStatus)
  return matched ?? null
}

const groupByStatus = (tasks, statuses) => {
  const grouped = statuses.reduce((accumulator, status) => {
    accumulator[status] = []
    return accumulator
  }, {})

  tasks.forEach(task => {
    const colStatus = mapTaskStatusToColumn(task.status, statuses)
    grouped[colStatus]?.push(task)
  })

  return grouped
}

const KanbanView = () => {
  const searchParams = useSearchParams()
  const paramProjectId = searchParams.get('projectId')
  const paramTaskId = searchParams.get('taskId')

  const [viewMode, setViewMode] = useState('kanban')
  const [projectId, setProjectId] = useState(paramProjectId || '')
  const [priorityFilter, setPriorityFilter] = useState('')
  const [assigneeFilter, setAssigneeFilter] = useState('')
  const [search, setSearch] = useState('')

  const [dialogOpen, setDialogOpen] = useState(false)
  const [columnsOpen, setColumnsOpen] = useState(false)
  const [selectedRecord, setSelectedRecord] = useState(null)
  const [recordToDelete, setRecordToDelete] = useState(null)

  const { data, isLoading, isError, error, refetch, createMutation, updateMutation, deleteMutation } = useCrudModule(
    'tasks',
    tasksService,
    { pageSize: 200, search }
  )

  const { data: lookups = EMPTY_LOOKUPS } = useLookups()

  const selectedProject = useMemo(
    () => (projectId ? (lookups.projects ?? []).find(project => project.id === Number(projectId)) : null),
    [projectId, lookups.projects]
  )

  const { columns } = useBoardColumns({
    projectKey: selectedProject?.key ?? null,
    templateCode: selectedProject?.typeCode ?? selectedProject?.templateCode,
    fallback: STATUSES
  })

  const statuses = useMemo(() => columns.map(column => column.status), [columns])

  const ability = useAbility()
  const canManageTasks = ability.can('manage', 'tasks')
  const canManageBoard = ability.can('manage', 'projects')

  const serverTasks = useMemo(() => data?.items ?? [], [data?.items])

  const filteredTasks = useMemo(() => {
    return serverTasks.filter(task => {
      if (projectId && task.projectId !== Number(projectId)) return false
      if (priorityFilter && task.priority !== Number(priorityFilter)) return false
      if (assigneeFilter && task.assignedToUserId !== Number(assigneeFilter)) return false

      if (search) {
        const query = search.toLowerCase()
        const titleMatch = task.title?.toLowerCase().includes(query)
        const descMatch = task.description?.toLowerCase().includes(query)

        if (!titleMatch && !descMatch) return false
      }

      return true
    })
  }, [serverTasks, projectId, priorityFilter, assigneeFilter, search])

  const baseBoard = useMemo(() => groupByStatus(filteredTasks, statuses), [filteredTasks, statuses])
  const [optimisticBoard, setOptimisticBoard] = useState(null)
  const board = optimisticBoard?.source === filteredTasks ? optimisticBoard.board : baseBoard

  const boardRef = useRef(board)
  boardRef.current = board

  const filteredTasksRef = useRef(filteredTasks)
  filteredTasksRef.current = filteredTasks

  const statusesRef = useRef(statuses)
  statusesRef.current = statuses

  const isDraggingRef = useRef(false)
  const pendingMoveRef = useRef(null)

  const { mutateAsync: updateTask } = updateMutation

  const [hasOpenedFromParam, setHasOpenedFromParam] = useState(false)

  useEffect(() => {
    if (paramTaskId && data?.items && !hasOpenedFromParam) {
      const task = data.items.find(item => item.id === Number(paramTaskId))

      if (task) {
        setSelectedRecord(task)
        setDialogOpen(true)
        setHasOpenedFromParam(true)
      }
    }
  }, [paramTaskId, data?.items, hasOpenedFromParam])

  const handleTaskMove = useCallback(
    async (task, toStatus) => {
      const apiStatus = mapColumnToTaskStatus(toStatus, statusesRef.current)

      if (!apiStatus) {
        toast.info(`Status "${toStatus}" is a display column; moving tasks to it is not supported yet.`)
        return
      }

      if (task.status === apiStatus) return

      const current = boardRef.current
      const fromStatus = mapTaskStatusToColumn(task.status, statusesRef.current)

      const optimistic = {
        ...current,
        [fromStatus]: (current[fromStatus] ?? []).filter(item => item.id !== task.id),
        [toStatus]: [...(current[toStatus] ?? []).filter(item => item.id !== task.id), { ...task, status: apiStatus }]
      }

      setOptimisticBoard({ source: filteredTasksRef.current, board: optimistic })

      try {
        await updateTask({ id: task.id, body: { ...task, status: apiStatus } })
      } catch (err) {
        setOptimisticBoard(null)
        toast.error(extractErrors(err.response?.data)[0] || 'Failed to update task status')
      }
    },
    [updateTask]
  )

  const handleSave = async formData => {
    const payload = { ...formData }

    if (payload.projectId) payload.projectId = Number(payload.projectId)
    if (payload.assignedToUserId) payload.assignedToUserId = Number(payload.assignedToUserId)
    if (payload.userStoryId) payload.userStoryId = Number(payload.userStoryId)
    if (payload.priority) payload.priority = Number(payload.priority)
    if (payload.estimatedHours != null) payload.estimatedHours = Number(payload.estimatedHours)
    if (payload.actualHours != null) payload.actualHours = Number(payload.actualHours)
    if (!payload.status) payload.status = 'ToDo'

    try {
      if (selectedRecord?.id) {
        await updateMutation.mutateAsync({ id: selectedRecord.id, body: { ...selectedRecord, ...payload } })
      } else {
        await createMutation.mutateAsync(payload)
      }

      setDialogOpen(false)
      setSelectedRecord(null)
    } catch {
      // Error handled by useCrudModule
    }
  }

  const handleTaskClick = useCallback(task => {
    setSelectedRecord(task)
    setDialogOpen(true)
  }, [])

  const projects = lookups.projects ?? []
  const users = lookups.users ?? []

  if (viewMode === 'table') {
    return (
      <div className='flex flex-col gap-4'>
        <div className='flex justify-end'>
          <ToggleButtonGroup
            value={viewMode}
            exclusive
            onChange={(_, val) => val && setViewMode(val)}
            size='small'
          >
            <ToggleButton value='kanban'>
              <i className='tabler-layout-kanban me-1' /> Kanban
            </ToggleButton>
            <ToggleButton value='table'>
              <i className='tabler-table me-1' /> Table
            </ToggleButton>
          </ToggleButtonGroup>
        </div>
        <ModuleTable moduleKey='tasks' subject='tasks' manageAction='manage' intro='Manage tasks in table view with pagination and search.' />
      </div>
    )
  }

  if (isLoading) {
    return (
      <div className='flex min-bs-[60vh] items-center justify-center'>
        <CircularProgress />
      </div>
    )
  }

  if (isError) {
    return (
      <div className='flex flex-col gap-6'>
        <PageHeader
          eyebrow={themeConfig.templateName}
          title={moduleMeta.tasks.title}
          description='Drag tasks across columns to update status, click a card to view or edit full details.'
        />
        <ErrorState title='Could not load the task board' error={error} onRetry={() => refetch()} />
      </div>
    )
  }

  const hasTasks = serverTasks.length > 0
  const totalCount = data?.totalCount ?? 0
  const isTruncated = totalCount > serverTasks.length

  return (
    <div className='flex flex-col gap-6'>
      <PageHeader
        eyebrow={themeConfig.templateName}
        title={moduleMeta.tasks.title}
        description='Drag tasks across columns to update status, click a card to view or edit full details.'
        actionLabel={canManageTasks ? `New ${moduleMeta.tasks.singular}` : null}
        onAction={() => {
          setSelectedRecord(projectId ? { projectId: Number(projectId), status: 'ToDo', active: true } : null)
          setDialogOpen(true)
        }}
        actionDisabled={createMutation.isPending}
      />

      <Card>
        <CardContent className='flex flex-col gap-4'>
          <div className='flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between'>
            <Typography variant='body2' color='text.secondary'>
              Filter tasks by project, priority, assignee, or search text.
            </Typography>
            <div className='flex flex-wrap items-center gap-3'>
              {canManageBoard ? (
                <Tooltip
                  title={
                    !selectedProject
                      ? 'Select a project from the filter below to customize its board columns'
                      : `Customize board columns for ${selectedProject.name}`
                  }
                >
                  <span>
                    <Button
                      size='small'
                      variant='tonal'
                      startIcon={<i className='tabler-columns' />}
                      disabled={!selectedProject}
                      onClick={() => setColumnsOpen(true)}
                    >
                      Manage board
                    </Button>
                  </span>
                </Tooltip>
              ) : null}
              <ToggleButtonGroup
                value={viewMode}
                exclusive
                onChange={(_, val) => val && setViewMode(val)}
                size='small'
              >
                <ToggleButton value='kanban'>
                  <i className='tabler-layout-kanban me-1' /> Kanban
                </ToggleButton>
                <ToggleButton value='table'>
                  <i className='tabler-table me-1' /> Table
                </ToggleButton>
              </ToggleButtonGroup>
            </div>
          </div>

          <div className='grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4'>
            <CustomTextField
              placeholder='Search title or description...'
              value={search}
              onChange={event => setSearch(event.target.value)}
              size='small'
            />

            <FormControl size='small' fullWidth>
              <InputLabel id='project-filter-label'>Project</InputLabel>
              <Select
                labelId='project-filter-label'
                label='Project'
                value={projectId}
                onChange={event => {
                  setProjectId(event.target.value)
                  setColumnsOpen(false)
                }}
              >
                <MenuItem value=''>All projects</MenuItem>
                {projects.map(project => (
                  <MenuItem key={project.id} value={project.id}>
                    {project.name}
                  </MenuItem>
                ))}
              </Select>
            </FormControl>

            <FormControl size='small' fullWidth>
              <InputLabel id='priority-filter-label'>Priority</InputLabel>
              <Select
                labelId='priority-filter-label'
                label='Priority'
                value={priorityFilter}
                onChange={event => setPriorityFilter(event.target.value)}
              >
                <MenuItem value=''>All priorities</MenuItem>
                {[1, 2, 3, 4, 5].map(p => (
                  <MenuItem key={p} value={p}>
                    {priorityLabel(p)}
                  </MenuItem>
                ))}
              </Select>
            </FormControl>

            <FormControl size='small' fullWidth>
              <InputLabel id='assignee-filter-label'>Assignee</InputLabel>
              <Select
                labelId='assignee-filter-label'
                label='Assignee'
                value={assigneeFilter}
                onChange={event => setAssigneeFilter(event.target.value)}
              >
                <MenuItem value=''>All assignees</MenuItem>
                {users.map(user => (
                  <MenuItem key={user.id} value={user.id}>
                    {user.displayName || user.userName}
                  </MenuItem>
                ))}
              </Select>
            </FormControl>
          </div>

          {isTruncated ? (
            <Typography variant='body2' color='warning.main' className='flex items-center gap-2'>
              <i className='tabler-alert-triangle' />
              Showing the first {serverTasks.length} of {totalCount} tasks. Refine the search to load the rest —
              the project, priority and assignee filters only apply to the tasks already loaded.
            </Typography>
          ) : null}
        </CardContent>
      </Card>

      {hasTasks ? (
        <KanbanBoard
          board={board}
          columns={columns}
          lookups={lookups}
          onTaskMove={handleTaskMove}
          onTaskClick={handleTaskClick}
          isDraggingRef={isDraggingRef}
          pendingMoveRef={pendingMoveRef}
        />
      ) : (
        <EmptyState
          title='No tasks found'
          description='Create your first task or change filter options.'
          icon='tabler-clipboard-off'
        />
      )}

      {canManageBoard && selectedProject ? (
        <BoardColumnManager
          open={columnsOpen}
          projectKey={selectedProject.key}
          projectName={selectedProject.name}
          templateCode={selectedProject.typeCode ?? selectedProject.templateCode}
          onClose={() => setColumnsOpen(false)}
        />
      ) : null}

      <Can I='manage' a='tasks'>
        <EntityDialog
          open={dialogOpen}
          title={selectedRecord?.id ? `Edit ${moduleMeta.tasks.singular}` : `Create ${moduleMeta.tasks.singular}`}
          fields={moduleMeta.tasks.formFields}
          record={selectedRecord}
          lookups={lookups}
          onClose={() => {
            setDialogOpen(false)
            setSelectedRecord(null)
          }}
          onSubmit={handleSave}
          onDelete={
            selectedRecord?.id
              ? () => {
                  setRecordToDelete(selectedRecord)
                  setDialogOpen(false)
                }
              : null
          }
          loading={createMutation.isPending || updateMutation.isPending}
          extraContent={
            selectedRecord?.id ? <CommentsPanel embedded entityType='Task' entityId={selectedRecord.id} /> : null
          }
        />

        <ConfirmDialog
          open={Boolean(recordToDelete)}
          title='Deactivate task'
          description='This keeps the task in history but removes it from active board views.'
          onClose={() => setRecordToDelete(null)}
          onConfirm={async () => {
            try {
              await deleteMutation.mutateAsync(recordToDelete.id)
              setRecordToDelete(null)
              setSelectedRecord(null)
            } catch {
              // Handled by useCrudModule
            }
          }}
          loading={deleteMutation.isPending}
        />
      </Can>
    </div>
  )
}

export default KanbanView
