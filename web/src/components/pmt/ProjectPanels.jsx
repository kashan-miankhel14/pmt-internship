'use client'

// React Imports
import { useState } from 'react'

// Next Imports
import Link from 'next/link'

// MUI Imports
import Card from '@mui/material/Card'
import CardContent from '@mui/material/CardContent'
import Typography from '@mui/material/Typography'
import List from '@mui/material/List'
import ListItem from '@mui/material/ListItem'
import ListItemText from '@mui/material/ListItemText'
import Grid from '@mui/material/Grid'
import Button from '@mui/material/Button'
import IconButton from '@mui/material/IconButton'
import MenuItem from '@mui/material/MenuItem'
import Dialog from '@mui/material/Dialog'
import DialogTitle from '@mui/material/DialogTitle'
import DialogContent from '@mui/material/DialogContent'
import DialogActions from '@mui/material/DialogActions'

// Third-party Imports
import { toast } from 'react-toastify'
import { useMutation, useQueryClient } from '@tanstack/react-query'

// Component Imports
import StatusChip from '@/components/pmt/StatusChip'
import CustomTextField from '@core/components/mui/TextField'

// Hook Imports
import { useGitLinks } from '@/hooks/useGitLinks'
import { useSprints } from '@/hooks/useSprints'
import { gitProviders } from '@/libs/enums'
import { extractErrors } from '@/libs/errors'
import { attachmentsService } from '@/services/attachments'

export const ProjectOverviewCard = ({ project, ownerName, departmentName }) => {
  if (!project) return null

  return (
    <Card>
      <CardContent className='grid gap-4 md:grid-cols-2'>
        <div className='flex flex-col gap-2'>
          <Typography variant='body2' color='text.secondary'>
            Project key
          </Typography>
          <Typography variant='h5'>{project.key}</Typography>
          <Typography color='text.secondary'>{project.description}</Typography>
        </div>
        <div className='grid gap-4 sm:grid-cols-2'>
          <div>
            <Typography variant='body2' color='text.secondary'>
              Owner
            </Typography>
            <Typography>{ownerName ?? '--'}</Typography>
          </div>
          <div>
            <Typography variant='body2' color='text.secondary'>
              Department
            </Typography>
            <Typography>{departmentName ?? '--'}</Typography>
          </div>
          <div>
            <Typography variant='body2' color='text.secondary'>
              Status
            </Typography>
            <StatusChip value={project.status} />
          </div>
          <div>
            <Typography variant='body2' color='text.secondary'>
              Timeline
            </Typography>
            <Typography>{project.startDate} to {project.targetDate}</Typography>
          </div>
        </div>
      </CardContent>
    </Card>
  )
}

export const ProjectEntityList = ({
  title,
  items = [],
  primaryKey = 'title',
  secondaryKey = 'description',
  chipKey,
  projectKey
}) => {
  // Ensure items is always an array to avoid errors when null is passed
  const safeItems = Array.isArray(items) ? items : [];
  // Issues carry a human key (`PMT-1`). The backend now returns it as `key`; when only the raw
  // `number` is present it is composed with the project key. Stories/tasks without either just
  // render their title unchanged.
  const issueKey = item => item.key ?? (projectKey && item.number != null ? `${projectKey}-${item.number}` : null)

  return (
    <Card>
      <CardContent>
        <Typography variant='h6' className='mb-4'>
          {title}
        </Typography>
        {(!safeItems || safeItems.length === 0) ? (
          <Typography variant='body2' color='text.secondary'>
            No {title.toLowerCase()} recorded for this project yet.
          </Typography>
        ) : (
          <List className='divide-y divide-[var(--border-color)]'>
            {safeItems.filter(Boolean).map(item => {
              const key = issueKey(item)
              const basePath = title.toLowerCase() === 'stories' ? '/stories' : title.toLowerCase() === 'tasks' ? '/tasks' : '/issues'
              const idParamName = title.toLowerCase() === 'stories' ? 'storyId' : title.toLowerCase() === 'tasks' ? 'taskId' : 'issueId'
              const itemUrl = `${basePath}?projectId=${item.projectId}&${idParamName}=${item.id}`

              return (
                <ListItem
                  key={item.id}
                  className='hover:bg-actionHover rounded-md px-3 py-2 transition-colors cursor-pointer'
                  component={Link}
                  href={itemUrl}
                >
                  <ListItemText
                    primary={
                      key ? (
                        <span className='flex items-center gap-2'>
                          <span className='font-mono text-xs font-medium text-textSecondary'>{key}</span>
                          <span>{item[primaryKey]}</span>
                        </span>
                      ) : (
                        item[primaryKey]
                      )
                    }
                    secondary={item[secondaryKey] || 'No description added yet.'}
                  />
                  {chipKey ? <StatusChip value={item[chipKey]} /> : null}
                </ListItem>
              )
            })}
          </List>
        )}
      </CardContent>
    </Card>
  )
}

const emptyGitLinkForm = {
  provider: gitProviders[0],
  repositoryUrl: '',
  referenceType: 'Repository',
  referenceId: '',
  referenceUrl: ''
}

export const MetadataPanels = ({ attachments, entityType = 'Project', entityId }) => {
  const [addOpen, setAddOpen] = useState(false)
  const [deleteTarget, setDeleteTarget] = useState(null)
  const [form, setForm] = useState(emptyGitLinkForm)
  const queryClient = useQueryClient()
  const { data: links = [], createMutation, deleteMutation } = useGitLinks({ entityType, entityId })

  const uploadMutation = useMutation({
    mutationFn: file => attachmentsService.upload({ entityType, entityId, file }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['project-detail', entityId] }),
    onError: error => toast.error(extractErrors(error.response?.data)[0])
  })

  const deleteAttachmentMutation = useMutation({
    mutationFn: id => attachmentsService.remove(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['project-detail', entityId] }),
    onError: error => toast.error(extractErrors(error.response?.data)[0])
  })

  const handleOpenAdd = () => {
    setForm(emptyGitLinkForm)
    setAddOpen(true)
  }

  const handleAddLink = async () => {
    try {
      await createMutation.mutateAsync({ ...form, entityType, entityId })
      setAddOpen(false)
    } catch (error) {
      toast.error(extractErrors(error.response?.data)[0])
    }
  }

  const handleDeleteLink = async () => {
    try {
      await deleteMutation.mutateAsync(deleteTarget.id)
      setDeleteTarget(null)
    } catch (error) {
      toast.error(extractErrors(error.response?.data)[0])
    }
  }

  const handleUpload = event => {
    const file = event.target.files?.[0]

    event.target.value = ''

    if (!file) return

    if (file.size > 20 * 1024 * 1024) {
      toast.error('Files must be 20 MB or smaller.')

      return
    }

    uploadMutation.mutate(file)
  }

  const handleDownload = async attachment => {
    try {
      const response = await attachmentsService.download(attachment.id)
      const url = URL.createObjectURL(response.data)
      const link = document.createElement('a')

      link.href = url
      link.download = attachment.fileName
      link.click()
      URL.revokeObjectURL(url)
    } catch (error) {
      toast.error(extractErrors(error.response?.data)[0])
    }
  }

  return (
    <Grid container spacing={6}>
      <Grid size={{ xs: 12, lg: 6 }}>
        <Card>
          <CardContent className='flex flex-col gap-4'>
            <div>
              <Typography variant='h6'>Attachments</Typography>
              <Typography variant='body2' color='text.secondary'>
                Upload files up to 20 MB and keep them linked to this project.
              </Typography>
            </div>
            <Button component='label' variant='outlined' startIcon={<i className='tabler-upload' />} disabled={uploadMutation.isPending}>
              {uploadMutation.isPending ? 'Uploading…' : 'Upload file'}
              <input hidden type='file' onChange={handleUpload} />
            </Button>
            {attachments.map(item => (
              <div key={item.id} className='flex items-center justify-between gap-3 rounded-lg border p-4'>
                <div>
                  <Typography className='font-medium'>{item.fileName}</Typography>
                  <Typography variant='body2' color='text.secondary'>
                    {item.contentType} · {(item.fileSize / 1024).toFixed(0)} KB
                  </Typography>
                </div>
                <div className='flex'>
                  <IconButton color='primary' onClick={() => handleDownload(item)} aria-label={`Download ${item.fileName}`}>
                    <i className='tabler-download' />
                  </IconButton>
                  <IconButton color='error' onClick={() => deleteAttachmentMutation.mutate(item.id)} disabled={deleteAttachmentMutation.isPending} aria-label={`Delete ${item.fileName}`}>
                    <i className='tabler-trash' />
                  </IconButton>
                </div>
              </div>
            ))}
            {!attachments.length ? (
              <Typography variant='body2' color='text.secondary'>
                No attachments recorded for this project.
              </Typography>
            ) : null}
          </CardContent>
        </Card>
      </Grid>
      <Grid size={{ xs: 12, lg: 6 }}>
        <Card>
          <CardContent className='flex flex-col gap-4'>
            <div className='flex items-start justify-between gap-4'>
              <div>
                <Typography variant='h6'>Git links</Typography>
                <Typography variant='body2' color='text.secondary'>
                  Repository references prepared for GitHub, GitLab and Azure DevOps.
                </Typography>
              </div>
              <Button size='small' variant='tonal' startIcon={<i className='tabler-plus' />} onClick={handleOpenAdd}>
                Add git link
              </Button>
            </div>
            {links.map(item => (
              <div key={item.id} className='relative rounded-lg border p-4'>
                <IconButton
                  size='small'
                  color='error'
                  className='absolute right-2 top-2'
                  onClick={() => setDeleteTarget(item)}
                  aria-label={`Delete git link ${item.referenceId}`}
                >
                  <i className='tabler-trash' />
                </IconButton>
                <Typography className='font-medium'>{item.referenceId}</Typography>
                <Typography variant='body2' color='text.secondary'>
                  {item.provider}
                  {item.referenceType ? ` · ${item.referenceType}` : ''}
                </Typography>
                <Typography variant='body2' color='primary.main'>
                  {item.repositoryUrl}
                </Typography>
                {item.referenceUrl ? (
                  <Typography variant='body2' color='text.secondary'>
                    {item.referenceUrl}
                  </Typography>
                ) : null}
              </div>
            ))}
            {!links.length ? (
              <Typography variant='body2' color='text.secondary'>
                No git links yet. Use “Add git link” to attach a repository reference.
              </Typography>
            ) : null}
          </CardContent>
        </Card>
      </Grid>

      <Dialog open={addOpen} onClose={() => setAddOpen(false)} fullWidth maxWidth='sm'>
        <DialogTitle>Add git link</DialogTitle>
        <DialogContent>
          <Grid container spacing={4} className='pt-1'>
            <Grid size={{ xs: 12, md: 6 }}>
              <CustomTextField
                select
                fullWidth
                label='Provider'
                value={form.provider}
                onChange={event => setForm({ ...form, provider: event.target.value })}
              >
                {gitProviders.map(provider => (
                  <MenuItem key={provider} value={provider}>
                    {provider}
                  </MenuItem>
                ))}
              </CustomTextField>
            </Grid>
            <Grid size={{ xs: 12, md: 6 }}>
              <CustomTextField
                fullWidth
                label='Reference type'
                value={form.referenceType}
                onChange={event => setForm({ ...form, referenceType: event.target.value })}
                placeholder='Repository'
              />
            </Grid>
            <Grid size={{ xs: 12 }}>
              <CustomTextField
                fullWidth
                label='Repository URL'
                value={form.repositoryUrl}
                onChange={event => setForm({ ...form, repositoryUrl: event.target.value })}
                placeholder='https://github.com/org/repo'
              />
            </Grid>
            <Grid size={{ xs: 12, md: 6 }}>
              <CustomTextField
                fullWidth
                label='Reference ID'
                value={form.referenceId}
                onChange={event => setForm({ ...form, referenceId: event.target.value })}
                placeholder='repo-name'
              />
            </Grid>
            <Grid size={{ xs: 12, md: 6 }}>
              <CustomTextField
                fullWidth
                label='Reference URL'
                value={form.referenceUrl}
                onChange={event => setForm({ ...form, referenceUrl: event.target.value })}
                placeholder='https://github.com/org/repo/tree/main'
              />
            </Grid>
          </Grid>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setAddOpen(false)}>Cancel</Button>
          <Button
            variant='contained'
            onClick={handleAddLink}
            disabled={!form.repositoryUrl || !form.referenceId || createMutation.isPending}
          >
            Add link
          </Button>
        </DialogActions>
      </Dialog>

      <Dialog open={Boolean(deleteTarget)} onClose={() => setDeleteTarget(null)} fullWidth maxWidth='xs'>
        <DialogTitle>Remove git link</DialogTitle>
        <DialogContent>
          <Typography color='text.secondary'>
            This removes the repository reference from {deleteTarget?.repositoryUrl ?? 'this project'}.
          </Typography>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setDeleteTarget(null)}>Cancel</Button>
          <Button color='error' variant='contained' onClick={handleDeleteLink} disabled={deleteMutation.isPending}>
            Delete
          </Button>
        </DialogActions>
      </Dialog>
    </Grid>
  )
}

const emptySprintForm = { name: '', goal: '', startDate: '', endDate: '' }

// Sprint.Status is a server-owned string moved through the start/complete actions. Its exact
// spelling is a backend assumption (Planned/Active/Completed), so state is inferred loosely by
// keyword rather than matched against a fixed enum — an unknown value simply offers "Start".
const sprintState = status => {
  const value = String(status ?? '').toLowerCase()

  return {
    isActive: value.includes('active') || value.includes('progress'),
    isCompleted: value.includes('complete') || value.includes('closed') || value.includes('done')
  }
}

const formatSprintDate = value => {
  if (!value) return ''

  const date = new Date(value)

  return Number.isNaN(date.getTime())
    ? ''
    : date.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })
}

/**
 * Compact sprint management for the project detail Sprints tab: lists the project's sprints with
 * their dates and status, and offers create / start / complete. Keyed by Projects.Key via
 * useSprints. Stays graceful when the sprints endpoint is empty or unavailable.
 */
export const SprintsPanel = ({ projectKey }) => {
  const [addOpen, setAddOpen] = useState(false)
  const [form, setForm] = useState(emptySprintForm)
  const { sprints, isLoading, createMutation, startMutation, completeMutation } = useSprints(projectKey)

  const handleCreate = async () => {
    try {
      await createMutation.mutateAsync(form)
      setAddOpen(false)
      setForm(emptySprintForm)
    } catch (error) {
      toast.error(extractErrors(error.response?.data)[0])
    }
  }

  return (
    <Card>
      <CardContent className='flex flex-col gap-4'>
        <div className='flex items-start justify-between gap-4'>
          <div>
            <Typography variant='h6'>Sprints</Typography>
            <Typography variant='body2' color='text.secondary'>
              Plan, start and complete sprints for this project.
            </Typography>
          </div>
          <Button
            size='small'
            variant='tonal'
            startIcon={<i className='tabler-plus' />}
            onClick={() => {
              setForm(emptySprintForm)
              setAddOpen(true)
            }}
          >
            New sprint
          </Button>
        </div>

        {isLoading ? (
          <Typography variant='body2' color='text.secondary'>
            Loading sprints…
          </Typography>
        ) : null}

        {!isLoading && !sprints.length ? (
          <Typography variant='body2' color='text.secondary'>
            No sprints yet. Use “New sprint” to plan the first one.
          </Typography>
        ) : null}

        {sprints.map(sprint => {
          const { isActive, isCompleted } = sprintState(sprint.status)
          const range = [formatSprintDate(sprint.startDate), formatSprintDate(sprint.endDate)].filter(Boolean).join(' → ')

          return (
            <div key={sprint.id} className='flex flex-wrap items-center justify-between gap-3 rounded-lg border p-4'>
              <div className='flex flex-col gap-1'>
                <div className='flex items-center gap-2'>
                  <Typography className='font-medium'>{sprint.name}</Typography>
                  {sprint.status ? <StatusChip value={sprint.status} /> : null}
                </div>
                {sprint.goal ? (
                  <Typography variant='body2' color='text.secondary'>
                    {sprint.goal}
                  </Typography>
                ) : null}
                {range ? (
                  <Typography variant='body2' color='text.secondary'>
                    {range}
                  </Typography>
                ) : null}
              </div>
              <div className='flex gap-2'>
                {!isActive && !isCompleted ? (
                  <Button
                    size='small'
                    variant='tonal'
                    startIcon={<i className='tabler-player-play' />}
                    disabled={startMutation.isPending}
                    onClick={() => startMutation.mutate(sprint.id)}
                  >
                    Start
                  </Button>
                ) : null}
                {isActive ? (
                  <Button
                    size='small'
                    variant='tonal'
                    color='success'
                    startIcon={<i className='tabler-flag-check' />}
                    disabled={completeMutation.isPending}
                    onClick={() => completeMutation.mutate(sprint.id)}
                  >
                    Complete
                  </Button>
                ) : null}
              </div>
            </div>
          )
        })}
      </CardContent>

      <Dialog open={addOpen} onClose={() => setAddOpen(false)} fullWidth maxWidth='sm'>
        <DialogTitle>New sprint</DialogTitle>
        <DialogContent>
          <Grid container spacing={4} className='pt-1'>
            <Grid size={{ xs: 12 }}>
              <CustomTextField
                fullWidth
                required
                label='Name'
                value={form.name}
                onChange={event => setForm({ ...form, name: event.target.value })}
                placeholder='Sprint 1'
              />
            </Grid>
            <Grid size={{ xs: 12 }}>
              <CustomTextField
                fullWidth
                multiline
                minRows={2}
                label='Goal'
                value={form.goal}
                onChange={event => setForm({ ...form, goal: event.target.value })}
                placeholder='What this sprint delivers.'
              />
            </Grid>
            <Grid size={{ xs: 12, md: 6 }}>
              <CustomTextField
                fullWidth
                type='date'
                label='Start date'
                value={form.startDate}
                onChange={event => setForm({ ...form, startDate: event.target.value })}
              />
            </Grid>
            <Grid size={{ xs: 12, md: 6 }}>
              <CustomTextField
                fullWidth
                type='date'
                label='End date'
                value={form.endDate}
                onChange={event => setForm({ ...form, endDate: event.target.value })}
              />
            </Grid>
          </Grid>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setAddOpen(false)}>Cancel</Button>
          <Button variant='contained' onClick={handleCreate} disabled={!form.name.trim() || createMutation.isPending}>
            Create sprint
          </Button>
        </DialogActions>
      </Dialog>
    </Card>
  )
}
