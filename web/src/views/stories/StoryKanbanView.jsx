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
import StoryKanbanBoard from '@/components/pmt/StoryKanbanBoard'
import BoardColumnManager from '@/components/pmt/BoardColumnManager'
import ModuleTable from '@/components/pmt/ModuleTable'
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
import { storiesService } from '@/services/stories'

// Fallback board columns for the global board (the "All projects" filter, where there is no
// single project context). A deliberate subset of the StoryStatus enum (mirrored by
// `storyStatuses` in @/libs/enums): 'Cancelled' is omitted so cancelled stories get no column.
// When a project IS selected these are replaced by its BoardColumns via useBoardColumns.
const STATUSES = ['Backlog', 'Ready', 'InProgress', 'Review', 'Done']

// Stable identity while the lookups are still loading: a `{}` default would be a new object on
// every render and would invalidate the memoised board below.
const EMPTY_LOOKUPS = {}

const mapStoryStatusToColumn = (storyStatus, columnStatuses) => {
  const normalizedStatuses = columnStatuses.map(s => String(s).toLowerCase())
  const hasSelected = normalizedStatuses.includes('selected')
  const hasToDo = normalizedStatuses.includes('todo')

  if (hasSelected) {
    if (storyStatus === 'Ready') {
      const idx = normalizedStatuses.indexOf('selected')
      return columnStatuses[idx]
    }
  } else if (hasToDo) {
    if (storyStatus === 'Backlog' || storyStatus === 'Ready' || storyStatus === 'Review') {
      const idx = normalizedStatuses.indexOf('todo')
      return columnStatuses[idx]
    }
  }

  const matchIdx = normalizedStatuses.indexOf(String(storyStatus).toLowerCase())
  if (matchIdx !== -1) {
    return columnStatuses[matchIdx]
  }

  return columnStatuses[0]
}

const mapColumnToStoryStatus = (colStatus, columnStatuses) => {
  const normalizedColStatus = String(colStatus).toLowerCase()
  if (normalizedColStatus === 'selected') return 'Ready'
  if (normalizedColStatus === 'todo') return 'Backlog'
  
  const standardStatuses = ['Backlog', 'Ready', 'InProgress', 'Review', 'Done', 'Cancelled']
  const matched = standardStatuses.find(s => s.toLowerCase() === normalizedColStatus)

  // No valid StoryStatus for this column (e.g. a custom column like "QA"): return null so
  // the caller skips the request instead of sending an invalid enum to the API (400).
  return matched ?? null
}

const groupByStatus = (stories, statuses) => {
  const grouped = statuses.reduce((accumulator, status) => {
    accumulator[status] = []

    return accumulator
  }, {})

  stories.forEach(story => {
    const colStatus = mapStoryStatusToColumn(story.status, statuses)

    grouped[colStatus]?.push(story)
  })

  return grouped
}

const StoryKanbanView = () => {
  const searchParams = useSearchParams()
  const paramProjectId = searchParams.get('projectId')
  const paramStoryId = searchParams.get('storyId')

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
    'stories',
    storiesService,
    // The story list endpoint only supports server-side `search` (plus paging) — there are no
    // projectId/priority/assignee query params on the API. Pushing `search` to the server (it is
    // debounced inside useCrudModule) trims the payload; the remaining filters stay client-side
    // against the loaded page, and the truncation notice below warns when that page is capped.
    { pageSize: 200, search }
  )

  const { data: lookups = EMPTY_LOOKUPS } = useLookups()

  // When a single project is chosen in the filter, drive the board off its seeded BoardColumns;
  // "All projects" (projectId === '') falls back to the hardcoded STATUSES. The project record
  // from the lookups carries the key the columns endpoint needs and the template that seeds them.
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

  const canManageStories = ability.can('manage', 'stories')

  // Board columns live under /projects/{key}/columns, which is a project-administration route.
  const canManageBoard = ability.can('manage', 'projects')

  const serverStories = useMemo(() => data?.items ?? [], [data?.items])

  const filteredStories = useMemo(() => {
    return serverStories.filter(story => {
      if (projectId && story.projectId !== Number(projectId)) return false
      if (priorityFilter && story.priority !== Number(priorityFilter)) return false
      if (assigneeFilter && story.assignedToUserId !== Number(assigneeFilter)) return false

      if (search) {
        const query = search.toLowerCase()
        const titleMatch = story.title?.toLowerCase().includes(query)
        const descMatch = story.description?.toLowerCase().includes(query)
        const acceptanceMatch = story.acceptanceCriteria?.toLowerCase().includes(query)

        if (!titleMatch && !descMatch && !acceptanceMatch) return false
      }

      return true
    })
  }, [serverStories, projectId, priorityFilter, assigneeFilter, search])

  // Base board derived straight from the filtered set. Deriving it (instead of writing board
  // state during render) keeps it correct on the very first render the Kanban columns mount,
  // which @formkit/drag-and-drop relies on to register its draggable nodes. A useEffect would
  // leave the columns empty for one render and desync the library (the DOM had cards but the
  // library saw 0 values), so cards could not be dragged.
  const baseBoard = useMemo(() => groupByStatus(filteredStories, statuses), [filteredStories, statuses])

  // Optimistic overlay applied while a drag's status write is in flight. It is tagged with the
  // filtered set it was built from, so it is discarded automatically once new data (or a filter
  // change) produces a different base board — no state has to be set during render to reset it.
  const [optimisticBoard, setOptimisticBoard] = useState(null)
  const board = optimisticBoard?.source === filteredStories ? optimisticBoard.board : baseBoard

  const boardRef = useRef(board)

  boardRef.current = board

  const filteredStoriesRef = useRef(filteredStories)

  filteredStoriesRef.current = filteredStories

  // The board's columns can change (a different project → a different template), so the drag
  // handler reads the current status set from a ref instead of closing over a stale array.
  const statusesRef = useRef(statuses)

  statusesRef.current = statuses

  const isDraggingRef = useRef(false)

  // Shared across all columns: the target recorded by the hovered column during a drag.
  // Consumed once by the source column's onDragend, so a move request fires only when the
  // drag actually ends over a column — never mid-drag, never on a cancelled drag.
  const pendingMoveRef = useRef(null)

  // `updateMutation` is a fresh object on every render, so depending on it rebuilt this
  // callback every time and defeated the memoised board. `mutateAsync` is referentially stable.
  const { mutateAsync: updateStory } = updateMutation

  const [hasOpenedFromParam, setHasOpenedFromParam] = useState(false)

  useEffect(() => {
    if (paramStoryId && data?.items && !hasOpenedFromParam) {
      const story = data.items.find(item => item.id === Number(paramStoryId))

      if (story) {
        setSelectedRecord(story)
        setDialogOpen(true)
        setHasOpenedFromParam(true)
      }
    }
  }, [paramStoryId, data?.items, hasOpenedFromParam])

  const handleStoryMove = useCallback(
    async (story, toStatus) => {
      // A drag that ends without a real drop target (released outside the board, or over the
      // gap between columns) must not fire a status write: there is no column to move to and
      // the card has already snapped back to its original column.
      if (!toStatus || !statusesRef.current.includes(toStatus)) return

      const mappedStatus = mapColumnToStoryStatus(toStatus, statusesRef.current)

      // A column with no valid StoryStatus (e.g. a custom column) can't be persisted — skip
      // the request so the API never receives an invalid status (400 ValidationException).
      if (!mappedStatus) return

      const source = filteredStoriesRef.current
      const snapshot = boardRef.current

      const nextBoard = {}

      for (const status of statusesRef.current) {
        nextBoard[status] = (snapshot[status] ?? []).filter(item => item.id !== story.id)
      }

      nextBoard[toStatus] = [{ ...story, status: toStatus }, ...(nextBoard[toStatus] ?? [])]

      setOptimisticBoard({ board: nextBoard, source })

      try {
        await updateStory({ id: story.id, body: { ...story, status: mappedStatus } })
      } catch (error) {
        setOptimisticBoard({ board: snapshot, source })
        toast.error(extractErrors(error.response?.data)[0])
      }
    },
    [updateStory]
  )

  const handleSave = async formData => {
    const payload = { ...formData }

    if (!payload.status) payload.status = 'Backlog'

    try {
      if (selectedRecord?.id) {
        await updateMutation.mutateAsync({ id: selectedRecord.id, body: { ...selectedRecord, ...payload } })
      } else {
        await createMutation.mutateAsync(payload)
      }

      setDialogOpen(false)
      setSelectedRecord(null)
    } catch {
      // Mutation error feedback is provided by the onError handler in useCrudModule.
    }
  }

  const handleStoryClick = useCallback(story => {
    setSelectedRecord(story)
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
        <ModuleTable moduleKey='stories' subject='stories' manageAction='manage' intro='Manage stories in table view with pagination and search.' />
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
          title={moduleMeta.stories.title}
          description='Drag stories across columns to update status, click a card to view or edit full details.'
        />
        <ErrorState title='Could not load the story board' error={error} onRetry={() => refetch()} />
      </div>
    )
  }

  const hasStories = serverStories.length > 0

  // The API caps the page at 200 rows. When more stories match than are loaded, the client-side
  // project/priority/assignee filters can only see the loaded slice, so warn the user.
  const totalCount = data?.totalCount ?? 0
  const isTruncated = totalCount > serverStories.length

  return (
    <div className='flex flex-col gap-6'>
      <PageHeader
        eyebrow={themeConfig.templateName}
        title={moduleMeta.stories.title}
        description='Drag stories across columns to update status, click a card to view or edit full details.'
        actionLabel={canManageStories ? `New ${moduleMeta.stories.singular}` : null}
        onAction={() => {
          setSelectedRecord(projectId ? { projectId: Number(projectId), status: 'Backlog', active: true } : null)
          setDialogOpen(true)
        }}
        actionDisabled={createMutation.isPending}
      />

      <Card>
        <CardContent className='flex flex-col gap-4'>
          <div className='flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between'>
            <Typography variant='body2' color='text.secondary'>
              Filter stories by project, priority, assignee, or search text.
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
              placeholder='Search title, description or acceptance criteria...'
              value={search}
              onChange={event => setSearch(event.target.value)}
              size='small'
            />

            <FormControl size='small' fullWidth>
              <InputLabel id='story-project-filter-label'>Project</InputLabel>
              <Select
                labelId='story-project-filter-label'
                label='Project'
                value={projectId}
                onChange={event => {
                  setProjectId(event.target.value)

                  // The column manager is scoped to one project; drop it when the scope changes.
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
              <InputLabel id='story-priority-filter-label'>Priority</InputLabel>
              <Select
                labelId='story-priority-filter-label'
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
              <InputLabel id='story-assignee-filter-label'>Assignee</InputLabel>
              <Select
                labelId='story-assignee-filter-label'
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
              Showing the first {serverStories.length} of {totalCount} stories. Refine the search to load the rest —
              the project, priority and assignee filters only apply to the stories already loaded.
            </Typography>
          ) : null}
        </CardContent>
      </Card>

      {hasStories ? (
        <StoryKanbanBoard
          board={board}
          columns={columns}
          lookups={lookups}
          onStoryMove={handleStoryMove}
          onStoryClick={handleStoryClick}
          isDraggingRef={isDraggingRef}
          pendingMoveRef={pendingMoveRef}
        />
      ) : (
        <EmptyState
          title='No stories found'
          description='Create your first story or change filter options.'
          icon='tabler-bulb-off'
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

      <Can I='manage' a='stories'>
        <EntityDialog
          open={dialogOpen}
          title={selectedRecord?.id ? `Edit ${moduleMeta.stories.singular}` : `Create ${moduleMeta.stories.singular}`}
          fields={moduleMeta.stories.formFields}
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
            selectedRecord?.id ? <CommentsPanel embedded entityType='UserStory' entityId={selectedRecord.id} /> : null
          }
        />

        <ConfirmDialog
          open={Boolean(recordToDelete)}
          title='Deactivate story'
          description='This keeps the story in history but removes it from active board views.'
          onClose={() => setRecordToDelete(null)}
          onConfirm={async () => {
            try {
              await deleteMutation.mutateAsync(recordToDelete.id)
              setRecordToDelete(null)
              setSelectedRecord(null)
            } catch {
              // Feedback comes from the onError handler in useCrudModule.
            }
          }}
          loading={deleteMutation.isPending}
        />
      </Can>
    </div>
  )
}

export default StoryKanbanView
