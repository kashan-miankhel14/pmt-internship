'use client'

// React Imports
import { memo, useCallback, useEffect, useMemo, useState } from 'react'

// Next Imports
import { useRouter, useSearchParams } from 'next/navigation'

// MUI Imports
import Card from '@mui/material/Card'
import CardContent from '@mui/material/CardContent'
import Table from '@mui/material/Table'
import TableBody from '@mui/material/TableBody'
import TableCell from '@mui/material/TableCell'
import TableContainer from '@mui/material/TableContainer'
import TableHead from '@mui/material/TableHead'
import TableRow from '@mui/material/TableRow'
import TablePagination from '@mui/material/TablePagination'
import IconButton from '@mui/material/IconButton'
import Tooltip from '@mui/material/Tooltip'
import Typography from '@mui/material/Typography'
import MenuItem from '@mui/material/MenuItem'
import Stack from '@mui/material/Stack'
import Avatar from '@mui/material/Avatar'
import CircularProgress from '@mui/material/CircularProgress'

// Component Imports
import CustomTextField from '@core/components/mui/TextField'
import CommentsPanel from '@/components/pmt/CommentsPanel'
import ConfirmDialog from '@/components/pmt/ConfirmDialog'
import EmptyState from '@/components/pmt/EmptyState'
import EntityDialog from '@/components/pmt/EntityDialog'
import ErrorState from '@/components/pmt/ErrorState'
import PageHeader from '@/components/pmt/PageHeader'
import StatusChip from '@/components/pmt/StatusChip'
import RoleDialog from '@/components/pmt/RoleDialog'

// Hook Imports
import { Can, useAbility } from '@/contexts/AbilityContext'
import { moduleMeta } from '@/libs/moduleMeta'
import { priorityLabel } from '@/libs/enums'
import { useCrudModule } from '@/hooks/useCrudModule'
import { useLookups } from '@/hooks/useLookups'

// Service imports
import { departmentsService } from '@/services/departments'
import { usersService } from '@/services/users'
import { projectsService } from '@/services/projects'
import { storiesService } from '@/services/stories'
import { tasksService } from '@/services/tasks'
import { issuesService } from '@/services/issues'

const serviceMap = {
  departments: departmentsService,
  users: usersService,
  projects: projectsService,
  stories: storiesService,
  tasks: tasksService,
  issues: issuesService
}

const commentEntityTypes = {
  tasks: 'Task',
  issues: 'Issue'
}

const EMPTY_INDEX = new Map()

const indexById = items => {
  if (!Array.isArray(items) || !items.length) return EMPTY_INDEX
  const index = new Map()
  for (const item of items) index.set(item.id, item)
  return index
}

const buildLookupIndex = lookups => ({
  users: indexById(lookups?.users),
  departments: indexById(lookups?.departments),
  roles: indexById(lookups?.roles),
  projects: indexById(lookups?.projects),
  tasks: indexById(lookups?.tasks),
  stories: indexById(lookups?.stories)
})

const formatCell = (value, column, lookupIndex) => {
  if (column === 'active') return <StatusChip value={value ? 'Active' : 'Closed'} />
  if (column === 'isLocked') return <StatusChip value={value ? 'Blocked' : 'Active'} />

  if (column === 'priority') return <StatusChip value={priorityLabel(value)} />
  if (['status', 'severity'].includes(column)) return <StatusChip value={value} />

  if (column.endsWith('UserId')) return lookupIndex.users.get(value)?.displayName ?? '--'
  if (column === 'departmentId') return lookupIndex.departments.get(value)?.name ?? '--'
  if (column === 'roleId') return lookupIndex.roles.get(value)?.name ?? '--'
  if (column === 'projectId') return lookupIndex.projects.get(value)?.name ?? '--'
  if (column === 'taskId') return lookupIndex.tasks.get(value)?.title ?? '--'
  if (column === 'userStoryId') return lookupIndex.stories.get(value)?.title ?? '--'

  return value === null || value === undefined || value === '' ? '--' : value
}

const titleCase = value => value.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/^./, value[0].toUpperCase())

const ModuleTableRow = memo(
  ({
    row,
    columns,
    lookupIndex,
    moduleKey,
    manageAction,
    subject,
    animationDelay,
    onOpen,
    onEdit,
    onDelete,
    onAssignRoles
  }) => (
    <TableRow hover className='pmt-fade-in transition-colors duration-150' style={{ animationDelay }}>
      {columns.map(column => (
        <TableCell key={`${row.id}-${column}`} className='py-2 px-3 text-xs text-textPrimary'>
          {formatCell(row[column], column, lookupIndex)}
        </TableCell>
      ))}
      <TableCell align='right' className='py-2 px-3'>
        <div className='flex items-center justify-end gap-0.5'>
          {moduleKey === 'projects' ? (
            <Tooltip title='Open Project Workspace'>
              <IconButton size='small' color='primary' onClick={() => onOpen(row)}>
                <i className='tabler-arrow-right text-base' />
              </IconButton>
            </Tooltip>
          ) : null}
          {moduleKey === 'users' ? (
            <Can I='manage' a='users'>
              <Tooltip title='Manage Roles'>
                <IconButton
                  size='small'
                  color='secondary'
                  onClick={() => onAssignRoles(row)}
                  aria-label={`Assign roles for ${row.displayName ?? row.userName}`}
                >
                  <i className='tabler-shield-check text-base' />
                </IconButton>
              </Tooltip>
            </Can>
          ) : null}
          <Can I={manageAction} a={subject}>
            <>
              <Tooltip title='Edit Record'>
                <IconButton size='small' color='primary' onClick={() => onEdit(row)}>
                  <i className='tabler-edit text-base' />
                </IconButton>
              </Tooltip>
              <Tooltip title='Deactivate Record'>
                <IconButton size='small' color='error' onClick={() => onDelete(row)}>
                  <i className='tabler-trash text-base' />
                </IconButton>
              </Tooltip>
            </>
          </Can>
        </div>
      </TableCell>
    </TableRow>
  )
)

const ModuleTable = ({ moduleKey, subject, manageAction = 'manage', intro }) => {
  const router = useRouter()
  const searchParams = useSearchParams()
  const paramIssueId = searchParams.get('issueId')
  const paramTaskId = searchParams.get('taskId')
  const paramStoryId = searchParams.get('storyId')
  const paramTargetId = paramIssueId || paramTaskId || paramStoryId
  const [hasOpenedFromParam, setHasOpenedFromParam] = useState(false)
  const config = moduleMeta[moduleKey]
  const [page, setPage] = useState(0)
  const [pageSize, setPageSize] = useState(10)
  const [search, setSearch] = useState('')
  const [selectedRecord, setSelectedRecord] = useState(null)
  const [recordToDelete, setRecordToDelete] = useState(null)
  const [dialogOpen, setDialogOpen] = useState(false)
  const [roleUser, setRoleUser] = useState(null)
  const { data: lookups } = useLookups()
  const ability = useAbility()

  const canManage = ability.can(manageAction, subject)
  const canAssignRoles = moduleKey === 'users' && ability.can('manage', 'users')
  const commentEntityType = commentEntityTypes[moduleKey]

  const service = serviceMap[moduleKey]

  const { data, isLoading, isError, error, refetch, createMutation, updateMutation, deleteMutation } = useCrudModule(
    moduleKey,
    service,
    {
      page: page + 1,
      pageSize,
      search
    }
  )

  const rows = data?.items ?? []

  useEffect(() => {
    if (paramTargetId && rows.length && !hasOpenedFromParam) {
      const record = rows.find(item => item.id === Number(paramTargetId))
      if (record) {
        setSelectedRecord(record)
        setDialogOpen(true)
        setHasOpenedFromParam(true)
      }
    }
  }, [paramTargetId, rows, hasOpenedFromParam])

  const columns = useMemo(() => config.columns, [config.columns])
  const lookupIndex = useMemo(() => buildLookupIndex(lookups), [lookups])

  const handleOpen = useCallback(row => router.push(`${config.route}/${row.id}`), [router, config.route])
  const handleEdit = useCallback(row => {
    setSelectedRecord(row)
    setDialogOpen(true)
  }, [])
  const handleDelete = useCallback(row => setRecordToDelete(row), [])
  const handleAssignRoles = useCallback(row => setRoleUser(row), [])

  const [loadCount, setLoadCount] = useState(0)
  useEffect(() => {
    setLoadCount(count => count + 1)
  }, [data])

  const handleSave = async formData => {
    try {
      if (selectedRecord?.id) {
        await updateMutation.mutateAsync({ id: selectedRecord.id, body: { ...selectedRecord, ...formData } })
      } else {
        await createMutation.mutateAsync(formData)
      }
      setDialogOpen(false)
      setSelectedRecord(null)
    } catch {
      // Handled by onError in useCrudModule
    }
  }

  const actionDisabled = createMutation.isPending || updateMutation.isPending

  return (
    <div className='flex flex-col gap-3.5'>
      <PageHeader
        eyebrow='PMT Workspace'
        title={config.title}
        description={intro}
        actionLabel={canManage ? `New ${config.singular}` : null}
        onAction={() => {
          setSelectedRecord(null)
          setDialogOpen(true)
        }}
        actionDisabled={actionDisabled}
      />

      <Card className='border border-[var(--mui-palette-divider)] shadow-sm'>
        <CardContent className='flex flex-col gap-4 p-5'>
          {/* Toolbar Header */}
          <div className='flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between'>
            <Stack direction='row' spacing={2} alignItems='center'>
              <Avatar className='bg-primary-lighter/20 text-primary-main' sx={{ width: 40, height: 40 }}>
                <i className='tabler-layout-grid text-xl' />
              </Avatar>
              <div>
                <Typography variant='h6' className='font-semibold text-textPrimary text-base'>
                  {config.title} Registry
                </Typography>
                <Typography variant='caption' color='text.secondary'>
                  Total {data?.totalCount ?? 0} records
                </Typography>
              </div>
            </Stack>

            <div className='flex flex-wrap items-center gap-3'>
              <CustomTextField
                value={search}
                onChange={event => {
                  setSearch(event.target.value)
                  setPage(0)
                }}
                placeholder={`Search ${config.title.toLowerCase()}...`}
                size='small'
                InputProps={{
                  startAdornment: <i className='tabler-search text-textSecondary mr-2 text-sm' />
                }}
                className='min-w-[240px]'
              />
              <CustomTextField
                select
                value={pageSize}
                onChange={event => {
                  setPage(0)
                  setPageSize(Number(event.target.value))
                }}
                size='small'
                className='min-w-[110px]'
              >
                {[10, 25, 50].map(option => (
                  <MenuItem key={option} value={option}>
                    {option} / page
                  </MenuItem>
                ))}
              </CustomTextField>
            </div>
          </div>

          {/* Table Content */}
          {isLoading ? (
            <div className='flex min-h-[260px] items-center justify-center'>
              <CircularProgress size={36} />
            </div>
          ) : isError ? (
            <ErrorState
              title={`Could not load ${config.title.toLowerCase()}`}
              error={error}
              onRetry={() => refetch()}
            />
          ) : rows.length ? (
            <TableContainer className='rounded-lg border border-[var(--mui-palette-divider)] overflow-hidden'>
              <Table size='small'>
                <TableHead className='bg-actionHover/50'>
                  <TableRow>
                    {columns.map(column => (
                      <TableCell
                        key={column}
                        className='py-3 font-bold text-xs uppercase tracking-wider text-textSecondary'
                      >
                        {titleCase(column)}
                      </TableCell>
                    ))}
                    <TableCell align='right' className='py-3 font-bold text-xs uppercase tracking-wider text-textSecondary'>
                      Actions
                    </TableCell>
                  </TableRow>
                </TableHead>
                <TableBody key={loadCount}>
                  {rows.map((row, index) => (
                    <ModuleTableRow
                      key={row.id}
                      row={row}
                      columns={columns}
                      lookupIndex={lookupIndex}
                      moduleKey={moduleKey}
                      manageAction={manageAction}
                      subject={subject}
                      animationDelay={`${index * 30}ms`}
                      onOpen={handleOpen}
                      onEdit={handleEdit}
                      onDelete={handleDelete}
                      onAssignRoles={handleAssignRoles}
                    />
                  ))}
                </TableBody>
              </Table>
              <TablePagination
                component='div'
                rowsPerPageOptions={[10, 25, 50]}
                count={data?.totalCount ?? 0}
                rowsPerPage={pageSize}
                page={page}
                onPageChange={(_, value) => setPage(value)}
                onRowsPerPageChange={event => {
                  setPage(0)
                  setPageSize(Number(event.target.value))
                }}
                className='border-t border-[var(--mui-palette-divider)]'
              />
            </TableContainer>
          ) : (
            <EmptyState
              title={`No ${config.title.toLowerCase()} found`}
              description='Search, filter or create a new record to get started.'
            />
          )}
        </CardContent>
      </Card>

      <Can I={manageAction} a={subject}>
        <EntityDialog
          open={dialogOpen}
          title={selectedRecord ? `Edit ${config.singular}` : `Create ${config.singular}`}
          fields={config.formFields}
          record={selectedRecord}
          lookups={lookups}
          onClose={() => {
            setDialogOpen(false)
            setSelectedRecord(null)
          }}
          onSubmit={handleSave}
          loading={createMutation.isPending || updateMutation.isPending}
          extraContent={
            commentEntityType && selectedRecord?.id ? (
              <CommentsPanel embedded entityType={commentEntityType} entityId={selectedRecord.id} />
            ) : null
          }
        />
      </Can>

      <ConfirmDialog
        open={Boolean(recordToDelete)}
        title={`Deactivate ${config.singular}`}
        description={`This removes the ${config.singular} from active lists.`}
        onClose={() => setRecordToDelete(null)}
        onConfirm={async () => {
          try {
            await deleteMutation.mutateAsync(recordToDelete.id)
            setRecordToDelete(null)
          } catch {
            // Handled in useCrudModule
          }
        }}
        loading={deleteMutation.isPending}
      />

      <RoleDialog
        open={canAssignRoles && Boolean(roleUser)}
        userId={roleUser?.id}
        userName={roleUser?.displayName ?? roleUser?.userName}
        onClose={() => setRoleUser(null)}
      />
    </div>
  )
}

export default ModuleTable
