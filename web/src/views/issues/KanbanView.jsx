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
import BoardColumnManager from '@/components/pmt/BoardColumnManager'
import IssueKanbanBoard from '@/components/pmt/IssueKanbanBoard'
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
import { issueSeverities, issueStatuses } from '@/libs/enums'

// Service Imports
import { issuesService } from '@/services/issues'

// Fallback board columns for the global board (the "All projects" filter, where there is no
// single project context): the full IssueStatus enum. When a project IS selected these are
// replaced by its BoardColumns via useBoardColumns, exactly like the task and story boards.
const STATUSES = issueStatuses

/*
  The board is the default view; the table is an opt-in toggle. Importing it statically pulled
  the whole registry module (its six services, the role dialog and the record dialog) into the
  /issues bundle for every visitor, so it is code-split behind the toggle instead.
*/
const ModuleTable = dynamic(() => import('@/components/pmt/ModuleTable'), {
  loading: () => (
    <div className='flex min-bs-[60vh] items-center justify-center'>
      <CircularProgress />
    </div>
  )
})

// Stable identity while the lookups are still loading: a `{}` default would be a new object on
// every render and would invalidate the memoised board below.
const EMPTY_LOOKUPS = {}

// Seeded/template board columns spell display names ('To Do', 'In Progress', 'Done', 'Selected',
// 'QA'), not the IssueStatus enum the API binds to, so a board driven by them has to be aliased
// in both directions: without it every issue collapses into lane 0 and a drag posts the column
// name verbatim, which the API rejects with a 400. Columns that already carry an enum value
// (the "All projects" fallback, whose columns are `issueStatuses` itself) match directly and
// never consult this table — that is what keeps its Closed and Rejected lanes writing
// Closed/Rejected instead of an alias.
const COLUMN_STATUS_ALIASES = new Map([
  ['todo', 'Open'],
  ['backlog', 'Open'],
  ['selected', 'Open'],
  ['inprogress', 'InProgress'],
  ['review', 'InProgress'],
  ['inreview', 'InProgress'],
  ['qa', 'InProgress'],
  ['done', 'Resolved']
])

// A seeded board rarely has a lane for every terminal status, so the ones it cannot spell are
// parked in the completion lane instead of collapsing back into lane 0.
const TERMINAL_LANE_STATUS = new Map([
  ['Closed', 'Resolved'],
  ['Rejected', 'Resolved']
])

const mapIssueStatusToColumn = (issueStatus, columnStatuses) => {
  const normalizedStatuses = columnStatuses.map(status => String(status).toLowerCase())
  const matchIdx = normalizedStatuses.indexOf(String(issueStatus).toLowerCase())

  if (matchIdx !== -1) {
    return columnStatuses[matchIdx]
  }

  const findAliasLane = target => normalizedStatuses.findIndex(status => COLUMN_STATUS_ALIASES.get(status) === target)

  // No lane spells this status: fall back to the first seeded lane that aliases onto it, so a
  // To Do / In Progress / Done board still spreads Open, InProgress and Resolved issues out.
  let aliasIdx = findAliasLane(issueStatus)

  if (aliasIdx === -1) {
    const terminalTarget = TERMINAL_LANE_STATUS.get(issueStatus)

    if (terminalTarget) aliasIdx = findAliasLane(terminalTarget)
  }

  return aliasIdx !== -1 ? columnStatuses[aliasIdx] : columnStatuses[0]
}

const mapColumnToIssueStatus = colStatus => {
  const normalizedColStatus = String(colStatus).toLowerCase()

  const matched = issueStatuses.find(status => status.toLowerCase() === normalizedColStatus)

  // Unknown column names resolve to the first IssueStatus rather than travelling as-is: the
  // status is deserialised straight onto the enum, so anything else comes back as a 400.
  return matched || COLUMN_STATUS_ALIASES.get(normalizedColStatus) || issueStatuses[0]
}

const groupByStatus = (issues, statuses) => {
  const grouped = statuses.reduce((accumulator, status) => {
    accumulator[status] = []

    return accumulator
  }, {})

  issues.forEach(issue => {
    const colStatus = mapIssueStatusToColumn(issue.status, statuses)

    grouped[colStatus]?.push(issue)
  })

  return grouped
}

const IssueKanbanView = () => {
  const searchParams = useSearchParams()
  const paramProjectId = searchParams.get('projectId')
  const paramIssueId = searchParams.get('issueId')

  const [viewMode, setViewMode] = useState('kanban')
  const [projectId, setProjectId] = useState(paramProjectId || '')

  // Issues have no priority in the API (UpsertIssueRequest carries Severity, not Priority), so
  // the triage filter is the IssueSeverity enum and it matches the raw string on the record.
  const [severityFilter, setSeverityFilter] = useState('')
  const [assigneeFilter, setAssigneeFilter] = useState('')
  const [search, setSearch] = useState('')

  const [dialogOpen, setDialogOpen] = useState(false)
  const [columnsOpen, setColumnsOpen] = useState(false)
  const [selectedRecord, setSelectedRecord] = useState(null)
  const [recordToDelete, setRecordToDelete] = useState(null)

  const { data, isLoading, isError, error, refetch, createMutation, updateMutation, deleteMutation } = useCrudModule(
    'issues',
    issuesService,
    // The issue list endpoint only supports server-side `search` (plus paging) — there are no
    // projectId/severity/assignee query params on the API. Pushing `search` to the server (it is
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

  // The API rejects issue writes without `issues.manage`, so the create affordance follows it.
  const canManageIssues = ability.can('manage', 'issues')

  // Board columns live under /projects/{key}/columns, which is a project-administration route.
  const canManageBoard = ability.can('manage', 'projects')

  const serverIssues = useMemo(() => data?.items ?? [], [data?.items])

  const filteredIssues = useMemo(() => {
    return serverIssues.filter(issue => {
      if (projectId && issue.projectId !== Number(projectId)) return false
      if (severityFilter && issue.severity !== severityFilter) return false
      if (assigneeFilter && issue.assignedToUserId !== Number(assigneeFilter)) return false

      if (search) {
        const query = search.toLowerCase()
        const titleMatch = issue.title?.toLowerCase().includes(query)
        const descMatch = issue.description?.toLowerCase().includes(query)
        const severityMatch = issue.severity?.toLowerCase().includes(query)

        if (!titleMatch && !descMatch && !severityMatch) return false
      }

      return true
    })
  }, [serverIssues, projectId, severityFilter, assigneeFilter, search])

  // Base board derived straight from the filtered set. Deriving it (instead of writing board
  // state during render) keeps it correct on the very first render the Kanban columns mount,
  // which @formkit/drag-and-drop relies on to register its draggable nodes.
  const baseBoard = useMemo(() => groupByStatus(filteredIssues, statuses), [filteredIssues, statuses])

  // Optimistic overlay applied while a drag's status write is in flight. It is tagged with the
  // filtered set it was built from, so it is discarded automatically once new data (or a filter
  // change) produces a different base board — no state has to be set during render to reset it.
  const [optimisticBoard, setOptimisticBoard] = useState(null)
  const board = optimisticBoard?.source === filteredIssues ? optimisticBoard.board : baseBoard

  const boardRef = useRef(board)

  boardRef.current = board

  const filteredIssuesRef = useRef(filteredIssues)

  filteredIssuesRef.current = filteredIssues

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
  const { mutateAsync: updateIssue } = updateMutation

  const [hasOpenedFromParam, setHasOpenedFromParam] = useState(false)

  useEffect(() => {
    if (paramIssueId && data?.items && !hasOpenedFromParam) {
      const issue = data.items.find(item => item.id === Number(paramIssueId))

      if (issue) {
        setSelectedRecord(issue)
        setDialogOpen(true)
        setHasOpenedFromParam(true)
      }
    }
  }, [paramIssueId, data?.items, hasOpenedFromParam])

  const handleIssueMove = useCallback(
    async (issue, toStatus) => {
      const source = filteredIssuesRef.current
      const snapshot = boardRef.current

      const nextBoard = {}

      // Remove the issue from every column first, so it can never end up listed
      // twice (which would render duplicate React keys and crash the board).
      for (const status of statusesRef.current) {
        nextBoard[status] = (snapshot[status] ?? []).filter(item => item.id !== issue.id)
      }

      nextBoard[toStatus] = [{ ...issue, status: toStatus }, ...(nextBoard[toStatus] ?? [])]

      setOptimisticBoard({ board: nextBoard, source })

      const mappedStatus = mapColumnToIssueStatus(toStatus)

      try {
        await updateIssue({ id: issue.id, body: { ...issue, status: mappedStatus } })
      } catch (error) {
        setOptimisticBoard({ board: snapshot, source })
        toast.error(extractErrors(error.response?.data)[0])
      }
    },
    [updateIssue]
  )

  const handleSave = async formData => {
    const payload = { ...formData }

    if (!payload.status) payload.status = 'Open'

    try {
      if (selectedRecord?.id) {
        await updateMutation.mutateAsync({ id: selectedRecord.id, body: { ...selectedRecord, ...payload } })
      } else {
        await createMutation.mutateAsync(payload)
      }

      setDialogOpen(false)
      setSelectedRecord(null)
    } catch {
      // Mutation error feedback is provided by the onError handler in useCrudModule; the
      // dialog stays open so the user can correct the record instead of losing the input.
    }
  }

  const handleIssueClick = useCallback(issue => {
    setSelectedRecord(issue)
    setDialogOpen(true)
  }, [])

  const projects = lookups.projects ?? []
  const users = lookups.users ?? []

  if (viewMode === 'table') {
    return (
      <div className='flex flex-col gap-4'>
        <div className='flex justify-end'>
          <ToggleButtonGroup value={viewMode} exclusive onChange={(_, val) => val && setViewMode(val)} size='small'>
            <ToggleButton value='kanban'>
              <i className='tabler-layout-kanban me-1' /> Kanban
            </ToggleButton>
            <ToggleButton value='table'>
              <i className='tabler-table me-1' /> Table
            </ToggleButton>
          </ToggleButtonGroup>
        </div>
        <ModuleTable
          moduleKey='issues'
          subject='issues'
          manageAction='manage'
          intro='Manage issues in table view with pagination and search.'
        />
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
          title={moduleMeta.issues.title}
          description='Drag issues across columns to update status, click a card to view or edit full details.'
        />
        <ErrorState title='Could not load the issue board' error={error} onRetry={() => refetch()} />
      </div>
    )
  }

  const hasIssues = serverIssues.length > 0

  // The API caps the page at 200 rows. When more issues match than are loaded, the client-side
  // project/severity/assignee filters can only see the loaded slice, so warn the user.
  const totalCount = data?.totalCount ?? 0
  const isTruncated = totalCount > serverIssues.length

  return (
    <div className='flex flex-col gap-6'>
      <PageHeader
        eyebrow={themeConfig.templateName}
        title={moduleMeta.issues.title}
        description='Drag issues across columns to update status, click a card to view or edit full details.'
        actionLabel={canManageIssues ? `New ${moduleMeta.issues.singular}` : null}
        onAction={() => {
          setSelectedRecord(projectId ? { projectId: Number(projectId), status: 'Open', active: true } : null)
          setDialogOpen(true)
        }}
        actionDisabled={createMutation.isPending}
      />

      <Card>
        <CardContent className='flex flex-col gap-4'>
          <div className='flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between'>
            <Typography variant='body2' color='text.secondary'>
              Filter issues by project, severity, assignee, or search text.
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
              <ToggleButtonGroup value={viewMode} exclusive onChange={(_, val) => val && setViewMode(val)} size='small'>
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
              placeholder='Search title, description or severity...'
              value={search}
              onChange={event => setSearch(event.target.value)}
              size='small'
            />

            <FormControl size='small' fullWidth>
              <InputLabel id='issue-project-filter-label'>Project</InputLabel>
              <Select
                labelId='issue-project-filter-label'
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
              <InputLabel id='issue-severity-filter-label'>Severity</InputLabel>
              <Select
                labelId='issue-severity-filter-label'
                label='Severity'
                value={severityFilter}
                onChange={event => setSeverityFilter(event.target.value)}
              >
                <MenuItem value=''>All severities</MenuItem>
                {issueSeverities.map(severity => (
                  <MenuItem key={severity} value={severity}>
                    {severity}
                  </MenuItem>
                ))}
              </Select>
            </FormControl>

            <FormControl size='small' fullWidth>
              <InputLabel id='issue-assignee-filter-label'>Assignee</InputLabel>
              <Select
                labelId='issue-assignee-filter-label'
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
              Showing the first {serverIssues.length} of {totalCount} issues. Refine the search to load the rest —
              the project, severity and assignee filters only apply to the issues already loaded.
            </Typography>
          ) : null}
        </CardContent>
      </Card>

      {hasIssues ? (
        <IssueKanbanBoard
          board={board}
          columns={columns}
          lookups={lookups}
          onIssueMove={handleIssueMove}
          onIssueClick={handleIssueClick}
          isDraggingRef={isDraggingRef}
          pendingMoveRef={pendingMoveRef}
        />
      ) : (
        <EmptyState
          title='No issues found'
          description='Create your first issue or change filter options.'
          icon='tabler-bug-off'
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

      <Can I='manage' a='issues'>
        <EntityDialog
          open={dialogOpen}
          title={selectedRecord?.id ? `Edit ${moduleMeta.issues.singular}` : `Create ${moduleMeta.issues.singular}`}
          fields={moduleMeta.issues.formFields}
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
            selectedRecord?.id ? <CommentsPanel embedded entityType='Issue' entityId={selectedRecord.id} /> : null
          }
        />

        <ConfirmDialog
          open={Boolean(recordToDelete)}
          title='Deactivate issue'
          description='This keeps the issue in history but removes it from active board views.'
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

export default IssueKanbanView
