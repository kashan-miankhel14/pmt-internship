'use client'

// React Imports
import { useState, useMemo } from 'react'

// Next Imports
import Link from 'next/link'

// MUI Imports
import Button from '@mui/material/Button'
import Card from '@mui/material/Card'
import CardContent from '@mui/material/CardContent'
import Chip from '@mui/material/Chip'
import CircularProgress from '@mui/material/CircularProgress'
import Dialog from '@mui/material/Dialog'
import DialogActions from '@mui/material/DialogActions'
import DialogContent from '@mui/material/DialogContent'
import DialogTitle from '@mui/material/DialogTitle'
import Grid from '@mui/material/Grid'
import IconButton from '@mui/material/IconButton'
import MenuItem from '@mui/material/MenuItem'
import Table from '@mui/material/Table'
import TableBody from '@mui/material/TableBody'
import TableCell from '@mui/material/TableCell'
import TableContainer from '@mui/material/TableContainer'
import TableHead from '@mui/material/TableHead'
import TableRow from '@mui/material/TableRow'
import Typography from '@mui/material/Typography'
import Tooltip from '@mui/material/Tooltip'
import Avatar from '@mui/material/Avatar'
import Tabs from '@mui/material/Tabs'
import Tab from '@mui/material/Tab'

// Third-party Imports
import { toast } from 'react-toastify'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'

// Component Imports
import CustomTextField from '@core/components/mui/TextField'

import ConfirmDialog from '@/components/pmt/ConfirmDialog'
import EmptyState from '@/components/pmt/EmptyState'
import ErrorState from '@/components/pmt/ErrorState'
import PageHeader from '@/components/pmt/PageHeader'
import StatusChip from '@/components/pmt/StatusChip'
import UserPicker from '@/components/pmt/UserPicker'
import TeamFormDialog from '@/views/teams/TeamFormDialog'

// Hook Imports
import { useAbility } from '@/contexts/AbilityContext'
import { useLookups } from '@/hooks/useLookups'
import { useTeam } from '@/hooks/useTeams'

// Services & Lib Imports
import { tasksService } from '@/services/tasks'
import { storiesService } from '@/services/stories'
import { issuesService } from '@/services/issues'
import { extractErrors } from '@/libs/errors'
import { priorityLabel } from '@/libs/enums'
import { teamRoles } from '@/libs/projectTemplates'

const formatDate = value => {
  if (!value) return '--'

  const date = new Date(value)

  return Number.isNaN(date.getTime()) ? '--' : date.toLocaleDateString()
}

const TeamDetail = ({ teamId }) => {
  const ability = useAbility()
  const queryClient = useQueryClient()
  const { data: lookups } = useLookups()

  const [editOpen, setEditOpen] = useState(false)
  const [addOpen, setAddOpen] = useState(false)
  const [selectedUsers, setSelectedUsers] = useState([])
  const [newMemberRole, setNewMemberRole] = useState('Member')
  const [memberToRemove, setMemberToRemove] = useState(null)
  const [workTab, setWorkTab] = useState('tasks')

  const {
    team,
    members,
    isLoading,
    isError,
    error,
    refetch,
    updateMutation,
    addMembersMutation,
    setMemberRoleMutation,
    removeMemberMutation
  } = useTeam(teamId)

  // Fetch team work items
  const { data: tasksData } = useQuery({
    queryKey: ['team-tasks', teamId],
    queryFn: () => tasksService.list({ pageSize: 200 })
  })

  const { data: storiesData } = useQuery({
    queryKey: ['team-stories', teamId],
    queryFn: () => storiesService.list({ pageSize: 200 })
  })

  const { data: issuesData } = useQuery({
    queryKey: ['team-issues', teamId],
    queryFn: () => issuesService.list({ pageSize: 200 })
  })

  const teamTasks = useMemo(() => {
    const all = tasksData?.items ?? []

    
return all.filter(t => String(t.teamId) === String(teamId))
  }, [tasksData, teamId])

  const teamStories = useMemo(() => {
    const all = storiesData?.items ?? []

    
return all.filter(s => String(s.teamId) === String(teamId))
  }, [storiesData, teamId])

  const teamIssues = useMemo(() => {
    const all = issuesData?.items ?? []

    
return all.filter(i => String(i.teamId) === String(teamId))
  }, [issuesData, teamId])

  const userMap = useMemo(() => {
    const map = {}

    ;(lookups?.users ?? []).forEach(u => {
      map[u.id] = u.displayName || u.email
    })
    
return map
  }, [lookups])

  // 1-Click Task status toggle
  const updateTaskMutation = useMutation({
    mutationFn: ({ id, data }) => tasksService.update(id, data),
    onSuccess: () => {
      toast.success('Task status updated')
      queryClient.invalidateQueries({ queryKey: ['team-tasks', teamId] })
    },
    onError: err => toast.error(extractErrors(err.response?.data)[0] || 'Failed to update task')
  })

  // Same rationale as the registry: the API has no `teams.*` claim yet, so the project grant
  // decides who may change team membership.
  const canManage = ability.can('manage', 'teams') || ability.can('manage', 'projects')

  const displayName = member =>
    member.displayName ?? member.userName ?? lookups?.users?.find(user => user.id === member.userId)?.displayName ?? '--'

  const email = member =>
    member.email ?? lookups?.users?.find(user => user.id === member.userId)?.email ?? '--'

  const memberUserIds = members.map(member => member.userId ?? member.id)

  const handleAddMembers = async () => {
    try {
      await addMembersMutation.mutateAsync({
        userIds: selectedUsers.map(user => user.id),
        teamRole: newMemberRole
      })
      toast.success(`${selectedUsers.length} member(s) added.`)
      setSelectedUsers([])
      setNewMemberRole('Member')
      setAddOpen(false)
    } catch {
      // Feedback comes from the onError handler in useTeam.
    }
  }

  const handleRoleChange = async (member, teamRole) => {
    try {
      await setMemberRoleMutation.mutateAsync({ userId: member.userId ?? member.id, teamRole })
    } catch {
      // The list is invalidated on success only, so a failure leaves the old role rendered.
    }
  }

  const handleRemove = async () => {
    try {
      await removeMemberMutation.mutateAsync(memberToRemove.userId ?? memberToRemove.id)
      setMemberToRemove(null)
    } catch {
      // Feedback comes from the onError handler in useTeam.
    }
  }

  const handleEditSubmit = async form => {
    try {
      await updateMutation.mutateAsync(form)
      toast.success(`${form.name} updated.`)
      setEditOpen(false)
    } catch {
      // Feedback comes from the onError handler in useTeam.
    }
  }

  if (isLoading) {
    return (
      <div className='flex min-bs-[60vh] items-center justify-center'>
        <CircularProgress />
      </div>
    )
  }

  if (isError || !team) {
    return (
      <div className='flex flex-col gap-6'>
        <PageHeader eyebrow='Team' title='Team' description='Members and project access for this team.' />
        <ErrorState title='Could not load this team' error={error} onRetry={() => refetch()} />
      </div>
    )
  }

  const lead =
    team.leadUserName ??
    lookups?.users?.find(user => user.id === team.leadUserId)?.displayName ??
    '--'

  return (
    <div className='flex flex-col gap-6'>
      <PageHeader
        eyebrow={`Team · ${team.key}`}
        title={team.name}
        description={team.description ?? 'Members of this team inherit every project role the team is granted.'}
        actionLabel={canManage ? 'Add members' : null}
        onAction={() => setAddOpen(true)}
        actionDisabled={addMembersMutation.isPending}
        actions={
          <>
            <Button component={Link} href='/teams' variant='tonal' startIcon={<i className='tabler-arrow-left' />}>
              All teams
            </Button>
            {canManage ? (
              <Button variant='tonal' onClick={() => setEditOpen(true)} startIcon={<i className='tabler-edit' />}>
                Edit team
              </Button>
            ) : null}
          </>
        }
      />

      <Card>
        <CardContent>
          <Grid container spacing={4}>
            <Grid size={{ xs: 6, md: 3 }}>
              <Typography variant='body2' color='text.secondary'>
                Key
              </Typography>
              <Chip size='small' variant='tonal' color='secondary' label={team.key} className='mbs-1' />
            </Grid>
            <Grid size={{ xs: 6, md: 3 }}>
              <Typography variant='body2' color='text.secondary'>
                Lead
              </Typography>
              <Typography className='mbs-1'>{lead}</Typography>
            </Grid>
            <Grid size={{ xs: 6, md: 3 }}>
              <Typography variant='body2' color='text.secondary'>
                Members
              </Typography>
              <Typography className='mbs-1'>{team.memberCount ?? members.length}</Typography>
            </Grid>
            <Grid size={{ xs: 6, md: 3 }}>
              <Typography variant='body2' color='text.secondary'>
                Status
              </Typography>
              <div className='mbs-1'>
                <StatusChip value={(team.active ?? team.isActive) ? 'Active' : 'Closed'} />
              </div>
            </Grid>
          </Grid>
        </CardContent>
      </Card>

      <Card>
        <CardContent className='flex flex-col gap-4'>
          <div>
            <Typography variant='h6'>Members</Typography>
            <Typography variant='body2' color='text.secondary'>
              A team must keep at least one Lead — the API rejects removing or demoting the last one.
            </Typography>
          </div>

          {members.length ? (
            <TableContainer>
              <Table>
                <TableHead>
                  <TableRow>
                    <TableCell>User name</TableCell>
                    <TableCell>Email</TableCell>
                    <TableCell className='min-is-[160px]'>Team role</TableCell>
                    <TableCell>Joined</TableCell>
                    <TableCell align='right'>Actions</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {members.map(member => {
                    const userId = member.userId ?? member.id

                    return (
                      <TableRow key={userId} hover>
                        <TableCell>{displayName(member)}</TableCell>
                        <TableCell>{email(member)}</TableCell>
                        <TableCell>
                          {canManage ? (
                            <CustomTextField
                              select
                              size='small'
                              value={member.teamRole ?? 'Member'}
                              onChange={event => handleRoleChange(member, event.target.value)}
                              disabled={setMemberRoleMutation.isPending}
                              className='min-is-[140px]'
                            >
                              {teamRoles.map(role => (
                                <MenuItem key={role} value={role}>
                                  {role}
                                </MenuItem>
                              ))}
                            </CustomTextField>
                          ) : (
                            <Chip size='small' variant='tonal' color='primary' label={member.teamRole ?? 'Member'} />
                          )}
                        </TableCell>
                        <TableCell>{formatDate(member.joinedAtUtc ?? member.joinedAt)}</TableCell>
                        <TableCell align='right'>
                          {canManage ? (
                            <IconButton
                              color='error'
                              onClick={() => setMemberToRemove(member)}
                              aria-label={`Remove ${displayName(member)}`}
                            >
                              <i className='tabler-user-minus' />
                            </IconButton>
                          ) : null}
                        </TableCell>
                      </TableRow>
                    )
                  })}
                </TableBody>
              </Table>
            </TableContainer>
          ) : (
            <EmptyState
              title='No members yet'
              description='Add people to this team so they inherit its project access.'
              icon='tabler-user-off'
            />
          )}
        </CardContent>
      </Card>

      {/* Team Work Items Card */}
      <Card>
        <CardContent className='flex flex-col gap-4'>
          <div className='flex flex-wrap items-center justify-between gap-3'>
            <div>
              <Typography variant='h6'>Assigned Work Items</Typography>
              <Typography variant='body2' color='text.secondary'>
                Tasks, stories, and issues assigned to {team.name}. Complete tasks with one click.
              </Typography>
            </div>
            <Tabs value={workTab} onChange={(e, val) => setWorkTab(val)}>
              <Tab value='tasks' label={`Tasks (${teamTasks.length})`} />
              <Tab value='stories' label={`Stories (${teamStories.length})`} />
              <Tab value='issues' label={`Issues (${teamIssues.length})`} />
            </Tabs>
          </div>

          {workTab === 'tasks' && (
            teamTasks.length === 0 ? (
              <EmptyState
                title='No tasks assigned to this team'
                description='Assign tasks to this team from any project Backlog, Active Board, or Task form.'
                icon='tabler-clipboard-check'
              />
            ) : (
              <TableContainer>
                <Table size='small'>
                  <TableHead>
                    <TableRow>
                      <TableCell padding='checkbox' />
                      <TableCell>Key</TableCell>
                      <TableCell>Title</TableCell>
                      <TableCell>Priority</TableCell>
                      <TableCell>Status</TableCell>
                      <TableCell>Individual Assignee</TableCell>
                      <TableCell>Due Date</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {teamTasks.map(task => {
                      const isDone = String(task.status).toLowerCase().includes('done')
                      const assigneeName = userMap[task.assignedToUserId || task.assigneeUserId] || 'Unassigned'

                      return (
                        <TableRow key={task.id} hover className={isDone ? 'bg-actionHover/20' : ''}>
                          <TableCell padding='checkbox'>
                            <Tooltip title={isDone ? 'Reopen task' : 'Mark task complete'}>
                              <IconButton
                                size='small'
                                onClick={() =>
                                  updateTaskMutation.mutate({
                                    id: task.id,
                                    data: { ...task, status: isDone ? 'ToDo' : 'Done' }
                                  })
                                }
                              >
                                {isDone ? (
                                  <i className='tabler-circle-check-filled text-success-main text-base' />
                                ) : (
                                  <i className='tabler-circle text-textDisabled hover:text-success-main text-base' />
                                )}
                              </IconButton>
                            </Tooltip>
                          </TableCell>
                          <TableCell className='font-mono text-xs font-bold text-textSecondary'>TASK-{task.id}</TableCell>
                          <TableCell>
                            <Typography className={`text-xs font-medium ${isDone ? 'line-through text-textDisabled' : 'text-textPrimary'}`}>
                              {task.title}
                            </Typography>
                          </TableCell>
                          <TableCell>
                            <StatusChip value={priorityLabel(task.priority)} size='small' />
                          </TableCell>
                          <TableCell>
                            <StatusChip value={task.status} size='small' />
                          </TableCell>
                          <TableCell>
                            <span className='text-xs font-medium text-textPrimary'>{assigneeName}</span>
                          </TableCell>
                          <TableCell className='text-xs font-mono text-textSecondary'>
                            {task.dueDate ? formatDate(task.dueDate) : '--'}
                          </TableCell>
                        </TableRow>
                      )
                    })}
                  </TableBody>
                </Table>
              </TableContainer>
            )
          )}

          {workTab === 'stories' && (
            teamStories.length === 0 ? (
              <EmptyState
                title='No user stories assigned to this team'
                description='Assign user stories to this team from the project Backlog.'
                icon='tabler-bookmark'
              />
            ) : (
              <TableContainer>
                <Table size='small'>
                  <TableHead>
                    <TableRow>
                      <TableCell>Key</TableCell>
                      <TableCell>Title</TableCell>
                      <TableCell>Points</TableCell>
                      <TableCell>Priority</TableCell>
                      <TableCell>Status</TableCell>
                      <TableCell>Individual Assignee</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {teamStories.map(story => (
                      <TableRow key={story.id} hover>
                        <TableCell className='font-mono text-xs font-bold text-primary-main'>STORY-{story.id}</TableCell>
                        <TableCell className='text-xs font-medium text-textPrimary'>{story.title}</TableCell>
                        <TableCell className='text-xs font-bold text-purple-600'>{story.storyPoints ?? 0} pts</TableCell>
                        <TableCell>
                          <StatusChip value={priorityLabel(story.priority)} size='small' />
                        </TableCell>
                        <TableCell>
                          <StatusChip value={story.status} size='small' />
                        </TableCell>
                        <TableCell className='text-xs font-medium text-textPrimary'>
                          {userMap[story.assignedToUserId || story.assigneeUserId] || 'Unassigned'}
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </TableContainer>
            )
          )}

          {workTab === 'issues' && (
            teamIssues.length === 0 ? (
              <EmptyState
                title='No issues assigned to this team'
                description='Assign bug reports or defects to this team from the project Issues tab.'
                icon='tabler-bug'
              />
            ) : (
              <TableContainer>
                <Table size='small'>
                  <TableHead>
                    <TableRow>
                      <TableCell>Key</TableCell>
                      <TableCell>Title</TableCell>
                      <TableCell>Severity</TableCell>
                      <TableCell>Status</TableCell>
                      <TableCell>Individual Assignee</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {teamIssues.map(issue => (
                      <TableRow key={issue.id} hover>
                        <TableCell className='font-mono text-xs font-bold text-error-main'>ISSUE-{issue.id}</TableCell>
                        <TableCell className='text-xs font-medium text-textPrimary'>{issue.title}</TableCell>
                        <TableCell>
                          <StatusChip value={issue.severity} size='small' />
                        </TableCell>
                        <TableCell>
                          <StatusChip value={issue.status} size='small' />
                        </TableCell>
                        <TableCell className='text-xs font-medium text-textPrimary'>
                          {userMap[issue.assignedToUserId] || 'Unassigned'}
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </TableContainer>
            )
          )}
        </CardContent>
      </Card>

      <TeamFormDialog
        open={editOpen}
        team={team}
        loading={updateMutation.isPending}
        onClose={() => setEditOpen(false)}
        onSubmit={handleEditSubmit}
      />

      <Dialog open={addOpen} onClose={() => setAddOpen(false)} fullWidth maxWidth='sm'>
        <DialogTitle>Add members</DialogTitle>
        <DialogContent>
          <Grid container spacing={4} className='pt-1'>
            <Grid size={{ xs: 12 }}>
              <UserPicker
                multiple
                label='Users'
                value={selectedUsers}
                onChange={setSelectedUsers}
                excludeIds={memberUserIds}
                helperText='People already in the team are hidden from the results.'
              />
            </Grid>
            <Grid size={{ xs: 12, sm: 6 }}>
              <CustomTextField
                select
                fullWidth
                label='Team role'
                value={newMemberRole}
                onChange={event => setNewMemberRole(event.target.value)}
              >
                {teamRoles.map(role => (
                  <MenuItem key={role} value={role}>
                    {role}
                  </MenuItem>
                ))}
              </CustomTextField>
            </Grid>
          </Grid>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setAddOpen(false)}>Cancel</Button>
          <Button
            variant='contained'
            onClick={handleAddMembers}
            disabled={!selectedUsers.length || addMembersMutation.isPending}
          >
            Add {selectedUsers.length || ''} member{selectedUsers.length === 1 ? '' : 's'}
          </Button>
        </DialogActions>
      </Dialog>

      <ConfirmDialog
        open={Boolean(memberToRemove)}
        title='Remove member'
        description={`${memberToRemove ? displayName(memberToRemove) : 'This user'} loses every project role inherited through ${team.name}.`}
        onClose={() => setMemberToRemove(null)}
        onConfirm={handleRemove}
        loading={removeMemberMutation.isPending}
        confirmLabel='Remove member'
      />
    </div>
  )
}

export default TeamDetail
