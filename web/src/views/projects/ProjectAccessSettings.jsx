'use client'

// React Imports
import { useState } from 'react'

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
import Tab from '@mui/material/Tab'
import Table from '@mui/material/Table'
import TableBody from '@mui/material/TableBody'
import TableCell from '@mui/material/TableCell'
import TableContainer from '@mui/material/TableContainer'
import TableHead from '@mui/material/TableHead'
import TableRow from '@mui/material/TableRow'
import Tabs from '@mui/material/Tabs'
import Typography from '@mui/material/Typography'

// Third-party Imports
import { toast } from 'react-toastify'

// Component Imports
import ConfirmDialog from '@/components/pmt/ConfirmDialog'
import EmptyState from '@/components/pmt/EmptyState'
import ErrorState from '@/components/pmt/ErrorState'
import PageHeader from '@/components/pmt/PageHeader'
import RoleSelect from '@/components/pmt/RoleSelect'
import TeamPicker from '@/components/pmt/TeamPicker'
import UserPicker from '@/components/pmt/UserPicker'

// Hook Imports
import { useAbility } from '@/contexts/AbilityContext'
import { useLookups } from '@/hooks/useLookups'
import { useProjectAccess, useProjectKey, useProjectRoles } from '@/hooks/useProjectAccess'

const formatDate = value => {
  if (!value) return '--'

  const date = new Date(value)

  return Number.isNaN(date.getTime()) ? '--' : date.toLocaleDateString()
}

const ProjectAccessSettings = ({ projectId }) => {
  const ability = useAbility()
  const { data: lookups } = useLookups()

  const [tab, setTab] = useState('members')
  const [addMemberOpen, setAddMemberOpen] = useState(false)
  const [addTeamOpen, setAddTeamOpen] = useState(false)
  const [selectedUsers, setSelectedUsers] = useState([])
  const [selectedTeam, setSelectedTeam] = useState(null)
  const [newRoleId, setNewRoleId] = useState(null)
  const [memberToRemove, setMemberToRemove] = useState(null)
  const [teamToRemove, setTeamToRemove] = useState(null)

  // The access endpoints are keyed by Projects.Key while the dashboard route carries the id.
  const { project, projectKey, isLoading: projectLoading, isError: projectError, error } = useProjectKey(projectId)

  const { roles } = useProjectRoles()

  const defaultRoleId = roles.find(role => role.name === 'Member')?.id ?? roles[0]?.id ?? null

  const {
    members,
    teams,
    membersQuery,
    teamsQuery,
    addMemberMutation,
    setMemberRoleMutation,
    removeMemberMutation,
    addTeamMutation,
    setTeamRoleMutation,
    removeTeamMutation
  } = useProjectAccess(projectKey)

  const canManage = ability.can('manage', 'projects')

  const memberName = member =>
    member.displayName ??
    member.userName ??
    lookups?.users?.find(user => user.id === member.userId)?.displayName ??
    '--'

  const memberEmail = member => member.email ?? lookups?.users?.find(user => user.id === member.userId)?.email ?? '--'

  const openAddMember = () => {
    setSelectedUsers([])
    setNewRoleId(defaultRoleId)
    setAddMemberOpen(true)
  }

  const openAddTeam = () => {
    setSelectedTeam(null)
    setNewRoleId(defaultRoleId)
    setAddTeamOpen(true)
  }

  const handleAddMembers = async () => {
    try {
      await addMemberMutation.mutateAsync({ userIds: selectedUsers.map(user => user.id), projectRoleId: newRoleId })
      toast.success(`${selectedUsers.length} member(s) granted access.`)
      setAddMemberOpen(false)
    } catch {
      // Feedback comes from the onError handler in useProjectAccess.
    }
  }

  const handleAddTeam = async () => {
    try {
      await addTeamMutation.mutateAsync({ teamId: selectedTeam?.id, projectRoleId: newRoleId })
      toast.success(`${selectedTeam?.name} granted access.`)
      setAddTeamOpen(false)
    } catch {
      // Feedback comes from the onError handler in useProjectAccess.
    }
  }

  if (ability.cannot('view', 'projects')) {
    return <EmptyState title='Unauthorized' description='You do not have permission to view this project.' />
  }

  if (projectLoading) {
    return (
      <div className='flex min-bs-[60vh] items-center justify-center'>
        <CircularProgress />
      </div>
    )
  }

  if (projectError || !projectKey) {
    return (
      <div className='flex flex-col gap-6'>
        <PageHeader eyebrow='Project settings' title='Access' description='Members and team grants for this project.' />
        <ErrorState
          title='Could not load this project'
          error={error}
          description={projectKey ? undefined : 'This project has no key, so its access grants cannot be resolved.'}
        />
      </div>
    )
  }

  return (
    <div className='flex flex-col gap-6'>
      <PageHeader
        eyebrow={`Project settings · ${projectKey}`}
        title='Access'
        description='Grant access directly to people, or to a whole team at once. When someone is granted through both paths the highest role wins.'
        actions={
          <Button
            component={Link}
            href={`/projects/${projectId}`}
            variant='tonal'
            startIcon={<i className='tabler-arrow-left' />}
          >
            {project?.name ?? 'Back to project'}
          </Button>
        }
      />

      <Card>
        <Tabs value={tab} onChange={(_, value) => setTab(value)}>
          <Tab value='members' label={`Members${members.length ? ` (${members.length})` : ''}`} />
          <Tab value='teams' label={`Teams${teams.length ? ` (${teams.length})` : ''}`} />
        </Tabs>
      </Card>

      {tab === 'members' ? (
        <Card>
          <CardContent className='flex flex-col gap-4'>
            <div className='flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between'>
              <div>
                <Typography variant='h6'>Direct members</Typography>
                <Typography variant='body2' color='text.secondary'>
                  A direct grant applies to one person and survives any team change.
                </Typography>
              </div>
              {canManage ? (
                <Button variant='contained' onClick={openAddMember} startIcon={<i className='tabler-user-plus' />}>
                  Add member
                </Button>
              ) : null}
            </div>

            {membersQuery.isLoading ? (
              <div className='flex min-bs-[200px] items-center justify-center'>
                <CircularProgress />
              </div>
            ) : membersQuery.isError ? (
              <ErrorState
                title='Could not load project members'
                error={membersQuery.error}
                onRetry={() => membersQuery.refetch()}
              />
            ) : members.length ? (
              <TableContainer>
                <Table>
                  <TableHead>
                    <TableRow>
                      <TableCell>User name</TableCell>
                      <TableCell>Email</TableCell>
                      <TableCell className='min-is-[200px]'>Project role</TableCell>
                      <TableCell>Added</TableCell>
                      <TableCell align='right'>Actions</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {members.map(member => {
                      const userId = member.userId ?? member.id

                      return (
                        <TableRow key={userId} hover>
                          <TableCell>{memberName(member)}</TableCell>
                          <TableCell>{memberEmail(member)}</TableCell>
                          <TableCell>
                            {canManage ? (
                              <RoleSelect
                                label={null}
                                className='min-is-[160px]'
                                value={member.projectRoleId}
                                disabled={setMemberRoleMutation.isPending}
                                onChange={projectRoleId =>
                                  setMemberRoleMutation.mutate({ userId, projectRoleId })
                                }
                              />
                            ) : (
                              <Chip
                                size='small'
                                variant='tonal'
                                color='primary'
                                label={
                                  member.projectRoleName ??
                                  roles.find(role => role.id === member.projectRoleId)?.name ??
                                  '--'
                                }
                              />
                            )}
                          </TableCell>
                          <TableCell>{formatDate(member.addedAtUtc ?? member.addedAt)}</TableCell>
                          <TableCell align='right'>
                            {canManage ? (
                              <IconButton
                                color='error'
                                onClick={() => setMemberToRemove(member)}
                                aria-label={`Remove ${memberName(member)}`}
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
                title='No direct members'
                description='Add people individually, or grant a whole team access from the Teams tab.'
                icon='tabler-user-off'
              />
            )}
          </CardContent>
        </Card>
      ) : null}

      {tab === 'teams' ? (
        <Card>
          <CardContent className='flex flex-col gap-4'>
            <div className='flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between'>
              <div>
                <Typography variant='h6'>Team grants</Typography>
                <Typography variant='body2' color='text.secondary'>
                  Every member of a granted team inherits its project role, including people added to the team later.
                </Typography>
              </div>
              {canManage ? (
                <Button variant='contained' onClick={openAddTeam} startIcon={<i className='tabler-users-plus' />}>
                  Grant team access
                </Button>
              ) : null}
            </div>

            {teamsQuery.isLoading ? (
              <div className='flex min-bs-[200px] items-center justify-center'>
                <CircularProgress />
              </div>
            ) : teamsQuery.isError ? (
              <ErrorState
                title='Could not load team grants'
                error={teamsQuery.error}
                onRetry={() => teamsQuery.refetch()}
              />
            ) : teams.length ? (
              <TableContainer>
                <Table>
                  <TableHead>
                    <TableRow>
                      <TableCell>Team name</TableCell>
                      <TableCell>Team key</TableCell>
                      <TableCell className='min-is-[200px]'>Project role</TableCell>
                      <TableCell align='right'>Members</TableCell>
                      <TableCell align='right'>Actions</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {teams.map(grant => {
                      const teamId = grant.teamId ?? grant.id

                      return (
                        <TableRow key={teamId} hover>
                          <TableCell>
                            <Link href={`/teams/${teamId}`} className='font-medium text-primary'>
                              {grant.teamName ?? grant.name ?? `Team #${teamId}`}
                            </Link>
                          </TableCell>
                          <TableCell>
                            <Chip size='small' variant='tonal' color='secondary' label={grant.teamKey ?? grant.key} />
                          </TableCell>
                          <TableCell>
                            {canManage ? (
                              <RoleSelect
                                label={null}
                                className='min-is-[160px]'
                                value={grant.projectRoleId}
                                disabled={setTeamRoleMutation.isPending}
                                onChange={projectRoleId => setTeamRoleMutation.mutate({ teamId, projectRoleId })}
                              />
                            ) : (
                              <Chip
                                size='small'
                                variant='tonal'
                                color='primary'
                                label={
                                  grant.projectRoleName ??
                                  roles.find(role => role.id === grant.projectRoleId)?.name ??
                                  '--'
                                }
                              />
                            )}
                          </TableCell>
                          <TableCell align='right'>{grant.memberCount ?? '--'}</TableCell>
                          <TableCell align='right'>
                            {canManage ? (
                              <IconButton
                                color='error'
                                onClick={() => setTeamToRemove(grant)}
                                aria-label={`Remove ${grant.teamName ?? grant.name} access`}
                              >
                                <i className='tabler-trash' />
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
                title='No teams have access'
                description='Grant a team access to onboard everyone in it at once.'
                icon='tabler-users-group'
              />
            )}
          </CardContent>
        </Card>
      ) : null}

      <Dialog open={addMemberOpen} onClose={() => setAddMemberOpen(false)} fullWidth maxWidth='sm'>
        <DialogTitle>Add member</DialogTitle>
        <DialogContent>
          <Grid container spacing={4} className='pt-1'>
            <Grid size={{ xs: 12 }}>
              <UserPicker
                multiple
                label='Users'
                value={selectedUsers}
                onChange={setSelectedUsers}
                excludeIds={members.map(member => member.userId ?? member.id)}
                helperText='People who already hold a direct grant are hidden from the results.'
              />
            </Grid>
            <Grid size={{ xs: 12, sm: 6 }}>
              <RoleSelect value={newRoleId} onChange={setNewRoleId} />
            </Grid>
          </Grid>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setAddMemberOpen(false)}>Cancel</Button>
          <Button
            variant='contained'
            onClick={handleAddMembers}
            disabled={!selectedUsers.length || !newRoleId || addMemberMutation.isPending}
          >
            Grant access
          </Button>
        </DialogActions>
      </Dialog>

      <Dialog open={addTeamOpen} onClose={() => setAddTeamOpen(false)} fullWidth maxWidth='sm'>
        <DialogTitle>Grant team access</DialogTitle>
        <DialogContent>
          <Grid container spacing={4} className='pt-1'>
            <Grid size={{ xs: 12 }}>
              <TeamPicker
                value={selectedTeam}
                onChange={setSelectedTeam}
                excludeIds={teams.map(grant => grant.teamId ?? grant.id)}
                helperText='Teams that already hold a grant are hidden from the results.'
              />
            </Grid>
            <Grid size={{ xs: 12, sm: 6 }}>
              <RoleSelect value={newRoleId} onChange={setNewRoleId} />
            </Grid>
          </Grid>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setAddTeamOpen(false)}>Cancel</Button>
          <Button
            variant='contained'
            onClick={handleAddTeam}
            disabled={!selectedTeam || !newRoleId || addTeamMutation.isPending}
          >
            Grant access
          </Button>
        </DialogActions>
      </Dialog>

      <ConfirmDialog
        open={Boolean(memberToRemove)}
        title='Remove member'
        description={`${memberToRemove ? memberName(memberToRemove) : 'This user'} loses direct access to this project. Access inherited through a team is unaffected.`}
        onClose={() => setMemberToRemove(null)}
        onConfirm={async () => {
          try {
            await removeMemberMutation.mutateAsync(memberToRemove.userId ?? memberToRemove.id)
            setMemberToRemove(null)
          } catch {
            // Feedback comes from the onError handler in useProjectAccess.
          }
        }}
        loading={removeMemberMutation.isPending}
        confirmLabel='Remove member'
      />

      <ConfirmDialog
        open={Boolean(teamToRemove)}
        title='Remove team access'
        description={`Every member of ${teamToRemove?.teamName ?? teamToRemove?.name ?? 'this team'} loses the access inherited from this grant, unless they are also a direct member.`}
        onClose={() => setTeamToRemove(null)}
        onConfirm={async () => {
          try {
            await removeTeamMutation.mutateAsync(teamToRemove.teamId ?? teamToRemove.id)
            setTeamToRemove(null)
          } catch {
            // Feedback comes from the onError handler in useProjectAccess.
          }
        }}
        loading={removeTeamMutation.isPending}
        confirmLabel='Remove access'
      />
    </div>
  )
}

export default ProjectAccessSettings
