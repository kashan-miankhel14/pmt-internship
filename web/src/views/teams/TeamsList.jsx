'use client'

// React Imports
import { useState } from 'react'

// Next Imports
import Link from 'next/link'
import { useRouter } from 'next/navigation'

// MUI Imports
import Avatar from '@mui/material/Avatar'
import Card from '@mui/material/Card'
import CardContent from '@mui/material/CardContent'
import Chip from '@mui/material/Chip'
import CircularProgress from '@mui/material/CircularProgress'
import IconButton from '@mui/material/IconButton'
import MenuItem from '@mui/material/MenuItem'
import Stack from '@mui/material/Stack'
import Table from '@mui/material/Table'
import TableBody from '@mui/material/TableBody'
import TableCell from '@mui/material/TableCell'
import TableContainer from '@mui/material/TableContainer'
import TableHead from '@mui/material/TableHead'
import TablePagination from '@mui/material/TablePagination'
import TableRow from '@mui/material/TableRow'
import Typography from '@mui/material/Typography'

// Third-party Imports
import { toast } from 'react-toastify'

// Component Imports
import ConfirmDialog from '@/components/pmt/ConfirmDialog'
import CustomTextField from '@core/components/mui/TextField'
import EmptyState from '@/components/pmt/EmptyState'
import ErrorState from '@/components/pmt/ErrorState'
import PageHeader from '@/components/pmt/PageHeader'
import StatusChip from '@/components/pmt/StatusChip'
import TeamFormDialog from '@/views/teams/TeamFormDialog'

// Hook Imports
import { useAbility } from '@/contexts/AbilityContext'
import { useLookups } from '@/hooks/useLookups'
import { useTeams } from '@/hooks/useTeams'

const TeamsList = () => {
  const router = useRouter()
  const ability = useAbility()

  const [page, setPage] = useState(0)
  const [pageSize, setPageSize] = useState(10)
  const [search, setSearch] = useState('')
  const [dialogOpen, setDialogOpen] = useState(false)
  const [selectedTeam, setSelectedTeam] = useState(null)
  const [teamToDelete, setTeamToDelete] = useState(null)

  const { data: lookups } = useLookups()

  const { items, totalCount, isLoading, isError, error, refetch, createMutation, updateMutation, deleteMutation } =
    useTeams({ page: page + 1, pageSize, search })

  /*
    The API permission catalogue has no `teams.*` claim yet (stage 2 is still landing on the
    server), so team management follows the project grant: whoever may manage projects may
    manage the teams that are granted access to them.
  */
  const canManage = ability.can('manage', 'teams') || ability.can('manage', 'projects')

  const leadName = team =>
    team.leadUserName ??
    team.leadDisplayName ??
    lookups?.users?.find(user => user.id === team.leadUserId)?.displayName ??
    '--'

  const handleSave = async form => {
    try {
      if (selectedTeam?.id) {
        await updateMutation.mutateAsync({ id: selectedTeam.id, body: form })
        toast.success(`${form.name} updated.`)
      } else {
        await createMutation.mutateAsync(form)
        toast.success(`${form.name} created.`)
      }

      setDialogOpen(false)
      setSelectedTeam(null)
    } catch {
      // Feedback comes from the onError handler in useTeams; the dialog stays open so the
      // input is not lost.
    }
  }

  const handleDelete = async () => {
    try {
      await deleteMutation.mutateAsync(teamToDelete.id)
      setTeamToDelete(null)
    } catch {
      // Feedback comes from the onError handler in useTeams.
    }
  }

  const saving = createMutation.isPending || updateMutation.isPending

  return (
    <div className='flex flex-col gap-6'>
      <PageHeader
        eyebrow='PMT Workspace'
        title='Teams'
        description='Teams group people once and are then granted access to projects as a unit. Every member of a team inherits the project role the team is granted.'
        actionLabel={canManage ? 'New team' : null}
        onAction={() => {
          setSelectedTeam(null)
          setDialogOpen(true)
        }}
        actionDisabled={saving}
      />

      <Card>
        <CardContent className='flex flex-col gap-4'>
          <div className='flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between'>
            <Stack direction='row' spacing={2} alignItems='center'>
              <Avatar className='bg-primaryLight text-primary'>
                <i className='tabler-users-group' />
              </Avatar>
              <div>
                <Typography variant='h6'>Team registry</Typography>
                <Typography variant='body2' color='text.secondary'>
                  Search and paging run server-side against the PMT API.
                </Typography>
              </div>
            </Stack>
            <div className='flex flex-col gap-3 sm:flex-row'>
              <CustomTextField
                value={search}
                onChange={event => {
                  setPage(0)
                  setSearch(event.target.value)
                }}
                placeholder='Search teams'
              />
              <CustomTextField
                select
                value={pageSize}
                onChange={event => {
                  setPage(0)
                  setPageSize(Number(event.target.value))
                }}
                className='min-is-[120px]'
              >
                {[10, 25, 50].map(option => (
                  <MenuItem key={option} value={option}>
                    {option} rows
                  </MenuItem>
                ))}
              </CustomTextField>
            </div>
          </div>

          {isLoading ? (
            <div className='flex min-bs-[280px] items-center justify-center'>
              <CircularProgress />
            </div>
          ) : isError ? (
            <ErrorState title='Could not load teams' error={error} onRetry={() => refetch()} />
          ) : items.length ? (
            <TableContainer>
              <Table>
                <TableHead>
                  <TableRow>
                    <TableCell>Name</TableCell>
                    <TableCell>Key</TableCell>
                    <TableCell>Lead</TableCell>
                    <TableCell align='right'>Members</TableCell>
                    <TableCell>Status</TableCell>
                    <TableCell align='right'>Actions</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {items.map((team, index) => (
                    <TableRow key={team.id} hover className='pmt-fade-in' style={{ animationDelay: `${index * 45}ms` }}>
                      <TableCell>
                        <Link href={`/teams/${team.id}`} className='font-medium text-primary'>
                          {team.name}
                        </Link>
                        {team.description ? (
                          <Typography variant='body2' color='text.secondary' className='line-clamp-1'>
                            {team.description}
                          </Typography>
                        ) : null}
                      </TableCell>
                      <TableCell>
                        <Chip size='small' variant='tonal' color='secondary' label={team.key} />
                      </TableCell>
                      <TableCell>{leadName(team)}</TableCell>
                      <TableCell align='right'>{team.memberCount ?? team.members?.length ?? 0}</TableCell>
                      <TableCell>
                        <StatusChip value={(team.active ?? team.isActive) ? 'Active' : 'Closed'} />
                      </TableCell>
                      <TableCell align='right'>
                        <div className='flex items-center justify-end gap-1'>
                          <IconButton
                            color='primary'
                            onClick={() => router.push(`/teams/${team.id}`)}
                            aria-label={`Open ${team.name}`}
                          >
                            <i className='tabler-arrow-right' />
                          </IconButton>
                          {canManage ? (
                            <>
                              <IconButton
                                color='primary'
                                onClick={() => {
                                  setSelectedTeam(team)
                                  setDialogOpen(true)
                                }}
                                aria-label={`Edit ${team.name}`}
                              >
                                <i className='tabler-edit' />
                              </IconButton>
                              <IconButton
                                color='error'
                                onClick={() => setTeamToDelete(team)}
                                aria-label={`Delete ${team.name}`}
                              >
                                <i className='tabler-trash' />
                              </IconButton>
                            </>
                          ) : null}
                        </div>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
              <TablePagination
                component='div'
                rowsPerPageOptions={[10, 25, 50]}
                count={totalCount}
                rowsPerPage={pageSize}
                page={page}
                onPageChange={(_, value) => setPage(value)}
                onRowsPerPageChange={event => {
                  setPage(0)
                  setPageSize(Number(event.target.value))
                }}
              />
            </TableContainer>
          ) : (
            <EmptyState
              title='No teams found'
              description='Create a team to group people once and grant them project access as a unit.'
              icon='tabler-users-group'
            />
          )}
        </CardContent>
      </Card>

      {canManage ? (
        <TeamFormDialog
          open={dialogOpen}
          team={selectedTeam}
          loading={saving}
          onClose={() => {
            setDialogOpen(false)
            setSelectedTeam(null)
          }}
          onSubmit={handleSave}
        />
      ) : null}

      <ConfirmDialog
        open={Boolean(teamToDelete)}
        title='Delete team'
        description={`Deleting ${teamToDelete?.name ?? 'this team'} removes its project access grants. Projects and their issues are kept, but members lose access unless they are also direct project members.`}
        onClose={() => setTeamToDelete(null)}
        onConfirm={handleDelete}
        loading={deleteMutation.isPending}
        confirmLabel='Delete team'
      />
    </div>
  )
}

export default TeamsList
