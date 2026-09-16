'use client'

import { useState, useEffect } from 'react'

import Link from 'next/link'

import Button from '@mui/material/Button'
import Avatar from '@mui/material/Avatar'
import Card from '@mui/material/Card'
import Tab from '@mui/material/Tab'
import Tabs from '@mui/material/Tabs'
import CircularProgress from '@mui/material/CircularProgress'
import Fade from '@mui/material/Fade'
import Chip from '@mui/material/Chip'
import Dialog from '@mui/material/Dialog'
import DialogTitle from '@mui/material/DialogTitle'
import DialogContent from '@mui/material/DialogContent'
import DialogActions from '@mui/material/DialogActions'
import MenuItem from '@mui/material/MenuItem'
import IconButton from '@mui/material/IconButton'
import Typography from '@mui/material/Typography'
import Skeleton from '@mui/material/Skeleton'

import { toast } from 'react-toastify'
import { useMutation, useQueryClient } from '@tanstack/react-query'

import CustomTextField from '@core/components/mui/TextField'

import CommentsPanel from '@/components/pmt/CommentsPanel'
import EmptyState from '@/components/pmt/EmptyState'
import ErrorState from '@/components/pmt/ErrorState'
import PageHeader from '@/components/pmt/PageHeader'
import StatusChip from '@/components/pmt/StatusChip'

import { MetadataPanels, ProjectOverviewCard } from '@/components/pmt/ProjectPanels'
import ProjectBacklogView from '@/views/projects/ProjectBacklogView'
import ProjectActiveBoardView from '@/views/projects/ProjectActiveBoardView'
import ProjectHierarchyView from '@/views/projects/ProjectHierarchyView'
import ProjectAnalyticsView from '@/views/projects/ProjectAnalyticsView'

import { useProjectDetail } from '@/hooks/useProjectDetail'
import { useLookups } from '@/hooks/useLookups'
import { useAbility } from '@/contexts/AbilityContext'
import { useAuth } from '@/contexts/AuthContext'
import { issuesService } from '@/services/issues'
import { extractErrors } from '@/libs/errors'
import { exportProjectToExcel } from '@/libs/excelExport'

const emptyIssueForm = {
  title: '',
  description: '',
  taskId: null,
  severity: 'Medium',
  status: 'Open',
  assignedToUserId: null,
  teamId: null
}

const ProjectDetail = ({ projectId }) => {
  const [tab, setTab] = useState('backlog') // Default to Backlog like Jira
  const ability = useAbility()
  const { session } = useAuth()
  const queryClient = useQueryClient()
  const { data, isLoading, isError, error, refetch } = useProjectDetail(projectId)
  const { data: lookups = {} } = useLookups()

  const [issueModal, setIssueModal] = useState({ open: false, isEdit: false, data: emptyIssueForm })

  const ownerName = lookups.users?.find(item => item.id === data?.project?.ownerUserId)?.displayName
  const departmentName = lookups.departments?.find(item => item.id === data?.project?.departmentId)?.name
  const projectKey = data?.project?.key
  const templateCode = data?.project?.typeCode || 'SCRUM'
  const stories = data?.stories ?? []
  const tasks = data?.tasks ?? []
  const issues = data?.issues ?? []
  const sprints = data?.sprints ?? []
  const usersList = lookups.users ?? []

  const handleExportExcel = () => {
    try {
      exportProjectToExcel({ ...data, sprints: data?.sprints || sprints }, lookups)
      toast.success('Project Excel report generated & downloaded!')
    } catch (err) {
      console.error(err)
      toast.error('Failed to export Excel report.')
    }
  }

  const userMap = {}

  usersList.forEach(u => {
    userMap[u.id] = u.displayName || u.email
  })

  const teamsList = lookups.teams ?? []
  const teamMap = {}

  teamsList.forEach(t => {
    teamMap[t.id] = t.name
  })

  const invalidateProject = () => {
    queryClient.invalidateQueries({ queryKey: ['project-detail', projectId] })
    queryClient.invalidateQueries({ queryKey: ['issues'] })
    queryClient.invalidateQueries({ queryKey: ['dashboard'] })
  }

  const createIssueMutation = useMutation({
    mutationFn: newIssue =>
      issuesService.create({
        ...newIssue,
        projectId,
        reportedByUserId: newIssue.reportedByUserId || session?.userId || session?.id || 1,
        active: true
      }),
    onSuccess: () => {
      toast.success('Issue reported')
      setIssueModal({ open: false, isEdit: false, data: emptyIssueForm })
      invalidateProject()
    },
    onError: err => toast.error(extractErrors(err.response?.data)[0] || 'Failed to create issue')
  })

  const updateIssueMutation = useMutation({
    mutationFn: ({ id, data: issueData }) => issuesService.update(id, { ...issueData, projectId }),
    onSuccess: () => {
      toast.success('Issue updated')
      setIssueModal({ open: false, isEdit: false, data: emptyIssueForm })
      invalidateProject()
    },
    onError: err => toast.error(extractErrors(err.response?.data)[0] || 'Failed to update issue')
  })

  const deleteIssueMutation = useMutation({
    mutationFn: id => issuesService.remove(id),
    onSuccess: () => {
      toast.success('Issue deleted')
      invalidateProject()
    },
    onError: err => toast.error(extractErrors(err.response?.data)[0] || 'Failed to delete issue')
  })

  useEffect(() => {
    if (isError) {
      console.error('ProjectDetail failed to load:', error)
    }
  }, [isError, error])

  if (ability.cannot('view', 'projects')) {
    return <EmptyState title='Unauthorized' description='You do not have permission to view projects.' />
  }

  if (isLoading) {
    return (
      <div className='flex flex-col gap-6 animate-pulse'>
        {/* Header skeleton */}
        <div className='flex flex-wrap items-center justify-between gap-4'>
          <div className='flex flex-col gap-2'>
            <Skeleton variant='text' width={180} height={20} />
            <Skeleton variant='text' width={320} height={40} />
            <div className='flex items-center gap-2'>
              <Skeleton variant='rounded' width={80} height={24} />
              <Skeleton variant='rounded' width={100} height={24} />
              <Skeleton variant='rounded' width={120} height={24} />
            </div>
          </div>
          <div className='flex items-center gap-2'>
            <Skeleton variant='rounded' width={130} height={38} />
            <Skeleton variant='rounded' width={140} height={38} />
          </div>
        </div>

        {/* Tab strip skeleton */}
        <Card className='border border-slate-200 bg-white p-2 shadow-xs'>
          <div className='flex gap-4 px-2 py-1'>
            <Skeleton variant='rounded' width={140} height={36} />
            <Skeleton variant='rounded' width={120} height={36} />
            <Skeleton variant='rounded' width={130} height={36} />
            <Skeleton variant='rounded' width={140} height={36} />
            <Skeleton variant='rounded' width={120} height={36} />
          </div>
        </Card>

        {/* Backlog / Content skeleton */}
        <div className='flex flex-col gap-4'>
          <Card className='border border-slate-200 bg-white p-6 shadow-sm flex flex-col gap-4'>
            <div className='flex items-center justify-between'>
              <Skeleton variant='text' width={220} height={28} />
              <Skeleton variant='rounded' width={100} height={28} />
            </div>
            <Skeleton variant='rounded' width='100%' height={56} />
            <Skeleton variant='rounded' width='100%' height={56} />
            <Skeleton variant='rounded' width='100%' height={56} />
          </Card>
        </div>
      </div>
    )
  }

  if (isError) {
    return <ErrorState title='Could not load project details' error={error} onRetry={() => refetch()} />
  }

  if (!data?.project) {
    return <EmptyState title='Project not found' description='This project may have been deleted, or you may not have access to it.' />
  }

  return (
    <div className='flex flex-col gap-6'>
      <PageHeader
        eyebrow={`Project Workspace · ${templateCode}`}
        title={data.project.name}
        description={data.project.description ?? 'Jira-style Agile workspace: Backlog, Sprints, Active Board, and Story Breakdowns.'}
        actions={
          <div className='flex items-center gap-3'>
            <Chip
              label={data.project.key}
              color='primary'
              variant='filled'
              className='font-mono font-bold text-sm px-1'
            />
            <Button
              variant='contained'
              color='success'
              className='font-bold shadow-sm bg-emerald-600 hover:bg-emerald-700 text-white'
              startIcon={<i className='tabler-file-spreadsheet text-lg' />}
              onClick={handleExportExcel}
            >
              Export Project Excel
            </Button>
            <Button
              component={Link}
              href={`/projects/${projectId}/settings/access`}
              variant='tonal'
              startIcon={<i className='tabler-settings' />}
            >
              Project Settings
            </Button>
          </div>
        }
      />

      {/* Jira-style Tab Navigation Bar */}
      <Card className='border border-slate-200 bg-white shadow-xs'>
        <Tabs
          value={tab}
          onChange={(_, value) => setTab(value)}
          variant='scrollable'
          scrollButtons='auto'
          className='px-2'
        >
          <Tab
            value='backlog'
            icon={<i className='tabler-list-details text-lg mr-1' />}
            iconPosition='start'
            label={
              <div className='flex items-center gap-1.5'>
                <span>Backlog & Sprints</span>
                <Chip size='small' label={stories.length} className='h-4 text-[10px]' />
              </div>
            }
          />
          <Tab
            value='board'
            icon={<i className='tabler-layout-kanban text-lg mr-1' />}
            iconPosition='start'
            label='Active Board'
          />
          <Tab
            value='hierarchy'
            icon={<i className='tabler-hierarchy text-lg mr-1' />}
            iconPosition='start'
            label={
              <div className='flex items-center gap-1.5'>
                <span>Stories & Tasks</span>
                <Chip size='small' label={`${tasks.length} tasks`} className='h-4 text-[10px]' />
              </div>
            }
          />
          <Tab
            value='analytics'
            icon={<i className='tabler-chart-dots text-lg mr-1' />}
            iconPosition='start'
            label='Analytics & Charts'
          />
          <Tab
            value='issues'
            icon={<i className='tabler-bug text-lg mr-1' />}
            iconPosition='start'
            label={
              <div className='flex items-center gap-1.5'>
                <span>Issues & Bugs</span>
                {issues.length > 0 && (
                  <Chip size='small' label={issues.length} color='error' className='h-4 text-[10px]' />
                )}
              </div>
            }
          />
          <Tab
            value='overview'
            icon={<i className='tabler-chart-pie text-lg mr-1' />}
            iconPosition='start'
            label='Summary & Assets'
          />
        </Tabs>
      </Card>

      {/* 1. BACKLOG & SPRINT PLANNING TAB */}
      {tab === 'backlog' && (
        <Fade in timeout={250}>
          <div>
            <ProjectBacklogView
              projectId={projectId}
              projectKey={projectKey}
              stories={stories}
              tasks={tasks}
              onRefresh={refetch}
            />
          </div>
        </Fade>
      )}

      {/* 2. ACTIVE BOARD TAB */}
      {tab === 'board' && (
        <Fade in timeout={250}>
          <div>
            <ProjectActiveBoardView
              projectId={projectId}
              projectKey={projectKey}
              templateCode={templateCode}
              stories={stories}
              tasks={tasks}
              onRefresh={refetch}
            />
          </div>
        </Fade>
      )}

      {/* 3. STORIES & TASKS HIERARCHY TAB */}
      {tab === 'hierarchy' && (
        <Fade in timeout={250}>
          <div>
            <ProjectHierarchyView
              projectId={projectId}
              projectKey={projectKey}
              stories={stories}
              tasks={tasks}
              onRefresh={refetch}
            />
          </div>
        </Fade>
      )}

      {/* 4. ANALYTICS & CHARTS TAB */}
      {tab === 'analytics' && (
        <Fade in timeout={250}>
          <div>
            <ProjectAnalyticsView
              projectId={projectId}
              projectKey={projectKey}
              stories={stories}
              tasks={tasks}
              issues={issues}
            />
          </div>
        </Fade>
      )}

      {/* 4. ISSUES & BUGS TRACKER TAB */}
      {tab === 'issues' && (
        <Fade in timeout={250}>
          <div className='flex flex-col gap-4'>
            <div className='flex items-center justify-between'>
              <div>
                <Typography variant='h6' className='font-bold text-slate-900'>
                  Bugs, Blockers & Defect Tracker
                </Typography>
                <Typography variant='body2' className='text-slate-500'>
                  Track defects and blockers linked directly to tasks or this project.
                </Typography>
              </div>
              {ability.can('manage', 'issues') && (
                <Button
                  variant='contained'
                  color='error'
                  startIcon={<i className='tabler-plus' />}
                  onClick={() => setIssueModal({ open: true, isEdit: false, data: emptyIssueForm })}
                >
                  Report Bug / Issue
                </Button>
              )}
            </div>

            <Card className='border border-slate-200 bg-white shadow-sm overflow-hidden'>
              <div className='divide-y divide-slate-100'>
                {issues.length === 0 ? (
                  <div className='p-12 text-center'>
                    <i className='tabler-circle-check text-4xl text-green-500 mb-2' />
                    <Typography variant='h6' className='font-bold text-slate-800'>
                      No bugs reported for this project
                    </Typography>
                    <Typography variant='body2' className='text-slate-500'>
                      Great job! Work is proceeding smoothly without active blockers.
                    </Typography>
                  </div>
                ) : (
                  issues.map(issue => {
                    const issueKeyStr = issue.key ?? (projectKey && issue.number ? `${projectKey}-${issue.number}` : `ISSUE-${issue.id}`)
                    const assignee = userMap[issue.assignedToUserId] || 'Unassigned'
                    const linkedTask = tasks.find(t => t.id === issue.taskId)

                    return (
                      <div
                        key={issue.id}
                        className='flex flex-wrap items-center justify-between gap-3 p-4 hover:bg-slate-50 transition-colors'
                      >
                        <div className='flex items-center gap-3 flex-1 min-w-[280px]'>
                          <Chip
                            size='small'
                            label={issueKeyStr}
                            color='error'
                            variant='tonal'
                            className='font-mono font-bold text-xs'
                          />
                          <div className='flex flex-col'>
                            <Typography className='font-bold text-sm text-slate-900'>
                              {issue.title}
                            </Typography>
                            {issue.description && (
                              <Typography variant='caption' className='text-slate-500 line-clamp-1'>
                                {issue.description}
                              </Typography>
                            )}
                            {linkedTask && (
                              <span className='text-[11px] text-primary-main font-mono mt-0.5'>
                                🔗 Blocks TASK-{linkedTask.id}: {linkedTask.title}
                              </span>
                            )}
                          </div>
                        </div>

                        <div className='flex items-center gap-2.5'>
                          <StatusChip value={issue.severity} />
                          <StatusChip value={issue.status} />

                          <div className='flex items-center gap-1.5 text-xs text-slate-800 bg-slate-100 border border-slate-200 px-2.5 py-1 rounded-md'>
                            <Avatar sx={{ width: 18, height: 18, fontSize: 10, bgcolor: '#6366f1' }}>{assignee[0]}</Avatar>
                            <span className='font-medium'>{assignee}</span>
                          </div>

                          {issue.teamId && teamMap[issue.teamId] && (
                            <span className='text-xs text-blue-700 bg-blue-50 border border-blue-200 px-2 py-0.5 rounded font-medium'>
                              👥 {teamMap[issue.teamId]}
                            </span>
                          )}

                          <IconButton
                            size='small'
                            onClick={() => setIssueModal({ open: true, isEdit: true, data: { ...emptyIssueForm, ...issue } })}
                            aria-label='Edit issue'
                          >
                            <i className='tabler-edit text-sm text-slate-600' />
                          </IconButton>
                          <IconButton
                            size='small'
                            color='error'
                            onClick={() => deleteIssueMutation.mutate(issue.id)}
                            aria-label='Delete issue'
                          >
                            <i className='tabler-trash text-sm' />
                          </IconButton>
                        </div>
                      </div>
                    )
                  })
                )}
              </div>
            </Card>

            {/* CREATE / EDIT ISSUE MODAL */}
            <Dialog open={issueModal.open} onClose={() => setIssueModal({ open: false, isEdit: false, data: emptyIssueForm })} fullWidth maxWidth='sm'>
              <DialogTitle>{issueModal.isEdit ? `Edit Issue #${issueModal.data?.id}` : 'Report Bug / Defect'}</DialogTitle>
              <DialogContent className='flex flex-col gap-4 pt-2'>
                <CustomTextField
                  label='Issue Title / Summary'
                  placeholder='e.g., Token expiration throws 500 error on checkout'
                  fullWidth
                  required
                  value={issueModal.data.title}
                  onChange={e => setIssueModal({ ...issueModal, data: { ...issueModal.data, title: e.target.value } })}
                />
                <CustomTextField
                  label='Steps to Reproduce / Details'
                  placeholder='Expected vs actual behavior…'
                  fullWidth
                  multiline
                  rows={3}
                  value={issueModal.data.description}
                  onChange={e => setIssueModal({ ...issueModal, data: { ...issueModal.data, description: e.target.value } })}
                />
                <div className='grid grid-cols-2 gap-4'>
                  <CustomTextField
                    select
                    label='Severity'
                    fullWidth
                    value={issueModal.data.severity || 'Medium'}
                    onChange={e => setIssueModal({ ...issueModal, data: { ...issueModal.data, severity: e.target.value } })}
                  >
                    <MenuItem value='Low'>Low</MenuItem>
                    <MenuItem value='Medium'>Medium</MenuItem>
                    <MenuItem value='High'>High</MenuItem>
                    <MenuItem value='Critical'>Critical</MenuItem>
                  </CustomTextField>
                  <CustomTextField
                    select
                    label='Status'
                    fullWidth
                    value={issueModal.data.status || 'Open'}
                    onChange={e => setIssueModal({ ...issueModal, data: { ...issueModal.data, status: e.target.value } })}
                  >
                    <MenuItem value='Open'>Open</MenuItem>
                    <MenuItem value='InProgress'>In Progress</MenuItem>
                    <MenuItem value='Resolved'>Resolved</MenuItem>
                    <MenuItem value='Closed'>Closed</MenuItem>
                  </CustomTextField>
                </div>
                <div className='grid grid-cols-1 md:grid-cols-3 gap-4'>
                  <CustomTextField
                    select
                    label='Linked Task (Optional)'
                    fullWidth
                    value={issueModal.data.taskId ? String(issueModal.data.taskId) : ''}
                    onChange={e => setIssueModal({ ...issueModal, data: { ...issueModal.data, taskId: e.target.value ? Number(e.target.value) : null } })}
                  >
                    <MenuItem value=''>None</MenuItem>
                    {tasks.map(t => (
                      <MenuItem key={t.id} value={String(t.id)}>
                        TASK-{t.id}: {t.title}
                      </MenuItem>
                    ))}
                  </CustomTextField>
                  <CustomTextField
                    select
                    label='Assignee'
                    fullWidth
                    value={issueModal.data.assignedToUserId ? String(issueModal.data.assignedToUserId) : ''}
                    onChange={e => setIssueModal({ ...issueModal, data: { ...issueModal.data, assignedToUserId: e.target.value ? Number(e.target.value) : null } })}
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
                    value={issueModal.data.teamId ? String(issueModal.data.teamId) : ''}
                    onChange={e => setIssueModal({ ...issueModal, data: { ...issueModal.data, teamId: e.target.value ? Number(e.target.value) : null } })}
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
                <Button onClick={() => setIssueModal({ open: false, isEdit: false, data: emptyIssueForm })}>Cancel</Button>
                <Button
                  variant='contained'
                  color='error'
                  disabled={!issueModal.data.title || createIssueMutation.isPending || updateIssueMutation.isPending}
                  onClick={() => {
                    if (issueModal.isEdit) {
                      updateIssueMutation.mutate({ id: issueModal.data.id, data: issueModal.data })
                    } else {
                      createIssueMutation.mutate(issueModal.data)
                    }
                  }}
                >
                  {issueModal.isEdit ? 'Update Issue' : 'Submit Issue'}
                </Button>
              </DialogActions>
            </Dialog>
          </div>
        </Fade>
      )}

      {/* 5. SUMMARY & ASSETS TAB */}
      {tab === 'overview' && (
        <Fade in timeout={250}>
          <div className='flex flex-col gap-6'>
            <ProjectOverviewCard project={data.project} ownerName={ownerName} departmentName={departmentName} />
            <MetadataPanels attachments={data.attachments ?? []} entityType='Project' entityId={projectId} />
            <CommentsPanel entityType='Project' entityId={projectId} />
          </div>
        </Fade>
      )}
    </div>
  )
}

export default ProjectDetail

