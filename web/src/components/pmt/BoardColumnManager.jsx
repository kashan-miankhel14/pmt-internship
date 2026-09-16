'use client'

// React Imports
import { useEffect, useState } from 'react'

// MUI Imports
import Button from '@mui/material/Button'
import CircularProgress from '@mui/material/CircularProgress'
import Dialog from '@mui/material/Dialog'
import DialogActions from '@mui/material/DialogActions'
import DialogContent from '@mui/material/DialogContent'
import DialogTitle from '@mui/material/DialogTitle'
import FormControlLabel from '@mui/material/FormControlLabel'
import IconButton from '@mui/material/IconButton'
import Switch from '@mui/material/Switch'
import Typography from '@mui/material/Typography'

// Third-party Imports
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { toast } from 'react-toastify'

// Component Imports
import ConfirmDialog from '@/components/pmt/ConfirmDialog'
import CustomTextField from '@core/components/mui/TextField'

// Hook Imports
import { useBoardColumns } from '@/hooks/useBoardColumns'

// Lib Imports
import { extractErrors } from '@/libs/errors'

// Service Imports
import { boardService } from '@/services/board'

// useBoardColumns memoises on the identity of `fallback`, so it has to be a stable array. The
// manager always edits a real project's board and has no hardcoded lanes of its own.
const EMPTY_FALLBACK = []

/*
  Every write is a full upsert: UpsertBoardColumnRequest(Name, Ordinal, CompleteColumn) in
  services/board.js. Renaming, reordering and flipping the complete flag therefore have to echo
  back the two fields they are not touching, otherwise the omitted ones travel as defaults.
*/
const toUpsert = (column, overrides = {}) => ({
  name: column.name,
  ordinal: column.ordinal,
  completeColumn: column.completeColumn,
  ...overrides
})

/**
 * Board column manager for one project.
 *
 * The project wizard promises the seeded board can be changed later; this is that screen. It
 * lists the columns in their left-to-right (ordinal) order and offers add / rename / move /
 * complete-flag / delete against `/projects/{key}/columns` through boardService.
 *
 * Columns are read through useBoardColumns, so the dialog shares the exact cache entry the
 * boards render from (`['board-columns', projectKey]`) and every mutation invalidates it — the
 * Kanban behind the dialog picks the new lanes up without a reload. When that hook is still
 * serving template defaults (rows without an `id`, i.e. nothing persisted came back from the
 * API yet) the per-row actions are disabled: there is no server-side row to address.
 */
const BoardColumnManager = ({ open, projectKey, projectName, templateCode, onClose }) => {
  const queryClient = useQueryClient()

  const { columns, isLoading, isError, refetch } = useBoardColumns({
    projectKey,
    templateCode,
    fallback: EMPTY_FALLBACK
  })

  const [newName, setNewName] = useState('')
  const [editingId, setEditingId] = useState(null)
  const [editingName, setEditingName] = useState('')
  const [columnToDelete, setColumnToDelete] = useState(null)

  // Drop the transient row state whenever the dialog is reopened or pointed at another project,
  // so a half-finished rename never leaks into the next board.
  useEffect(() => {
    setNewName('')
    setEditingId(null)
    setEditingName('')
    setColumnToDelete(null)
  }, [open, projectKey])

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: ['board-columns', projectKey] })

    // The project detail page renders the same board metadata in its tabs.
    queryClient.invalidateQueries({ queryKey: ['project-detail'] })
  }

  // Same contract as useCrudModule/useSprints: a 403 is handled by the AuthContext redirect,
  // every other failure surfaces as a single toast line.
  const handleError = error => {
    if (error.response?.status === 403) return

    toast.error(extractErrors(error.response?.data ?? { errors: [error.message] })[0])
  }

  const mutation = mutationFn => ({ mutationFn, onSuccess: invalidate, onError: handleError })

  const addMutation = useMutation(mutation(body => boardService.addColumn(projectKey, body)))
  const updateMutation = useMutation(mutation(({ id, body }) => boardService.updateColumn(projectKey, id, body)))
  const removeMutation = useMutation(mutation(id => boardService.removeColumn(projectKey, id)))

  // Reordering is two (or more) ordinal writes. They run in one mutation so the list is
  // invalidated once, at the end, instead of refetching between the halves of a swap.
  const reorderMutation = useMutation(
    mutation(async writes => {
      for (const write of writes) {
        await boardService.updateColumn(projectKey, write.id, write.body)
      }
    })
  )

  const isBusy =
    addMutation.isPending || updateMutation.isPending || removeMutation.isPending || reorderMutation.isPending

  // Rows without an id are the template fallback useBoardColumns synthesises while the API has
  // not returned real columns; they cannot be renamed, moved or deleted.
  const isTemplateFallback = columns.length > 0 && columns.some(column => !column.id)

  const handleAdd = () => {
    const name = newName.trim()

    if (!name) return

    addMutation.mutate(
      { name, ordinal: columns.length, completeColumn: false },
      { onSuccess: () => setNewName('') }
    )
  }

  const handleRenameStart = column => {
    setEditingId(column.id)
    setEditingName(column.name)
  }

  const handleRenameSave = column => {
    const name = editingName.trim()

    if (!name || name === column.name) {
      setEditingId(null)

      return
    }

    updateMutation.mutate({ id: column.id, body: toUpsert(column, { name }) }, { onSuccess: () => setEditingId(null) })
  }

  const handleToggleComplete = (column, checked) =>
    updateMutation.mutate({ id: column.id, body: toUpsert(column, { completeColumn: checked }) })

  const handleMove = (index, direction) => {
    const target = index + direction

    if (target < 0 || target >= columns.length) return

    const next = [...columns]
    const [moved] = next.splice(index, 1)

    next.splice(target, 0, moved)

    // The stored ordinals are not guaranteed to be a dense 0..n-1 range, so the whole list is
    // renumbered by position and only the rows whose ordinal actually changed are written.
    const writes = next
      .map((column, ordinal) => ({ column, ordinal }))
      .filter(entry => entry.column.ordinal !== entry.ordinal)
      .map(entry => ({ id: entry.column.id, body: toUpsert(entry.column, { ordinal: entry.ordinal }) }))

    if (!writes.length) return

    reorderMutation.mutate(writes)
  }

  return (
    <>
      <Dialog open={open} onClose={onClose} fullWidth maxWidth='sm'>
        <DialogTitle>Manage board columns{projectName ? ` — ${projectName}` : ''}</DialogTitle>
        <DialogContent>
          <div className='flex flex-col gap-4 pt-1'>
            <Typography variant='body2' color='text.secondary'>
              Columns are listed in board order, left to right. Renaming a column keeps the cards that already
              sit in it; deleting one removes the lane from every board of this project.
            </Typography>

            {isLoading ? (
              <div className='flex min-bs-[120px] items-center justify-center'>
                <CircularProgress />
              </div>
            ) : null}

            {isError ? (
              <div className='flex flex-wrap items-center justify-between gap-3 rounded-lg border p-4'>
                <Typography variant='body2' color='error.main'>
                  Could not load the board columns for this project.
                </Typography>
                <Button size='small' variant='tonal' onClick={() => refetch()}>
                  Retry
                </Button>
              </div>
            ) : null}

            {!isLoading && !isError && !columns.length ? (
              <Typography variant='body2' color='text.secondary'>
                This project has no board columns yet. Add the first one below.
              </Typography>
            ) : null}

            {isTemplateFallback ? (
              <Typography variant='body2' color='warning.main' className='flex items-center gap-2'>
                <i className='tabler-alert-triangle' />
                These are the template defaults — the API has not stored columns for this project yet, so they
                cannot be renamed, reordered or deleted. Adding a column creates a real one.
              </Typography>
            ) : null}

            {columns.map((column, index) => {
              const isEditing = editingId != null && editingId === column.id
              const isPersisted = Boolean(column.id)
              const rowDisabled = isBusy || !isPersisted

              return (
                <div
                  key={column.id ?? `${column.name}-${index}`}
                  className='flex flex-wrap items-center gap-3 rounded-lg border p-4'
                >
                  <span className='flex is-6 bs-6 items-center justify-center rounded-full bg-actionHover text-xs font-medium text-textSecondary'>
                    {index + 1}
                  </span>

                  <div className='flex min-is-[160px] flex-1 items-center gap-2'>
                    {isEditing ? (
                      <CustomTextField
                        fullWidth
                        autoFocus
                        size='small'
                        value={editingName}
                        onChange={event => setEditingName(event.target.value)}
                        onKeyDown={event => {
                          if (event.key === 'Enter') handleRenameSave(column)
                          if (event.key === 'Escape') setEditingId(null)
                        }}
                      />
                    ) : (
                      <Typography className='font-medium'>{column.name}</Typography>
                    )}
                  </div>

                  <FormControlLabel
                    label='Complete'
                    control={
                      <Switch
                        size='small'
                        checked={Boolean(column.completeColumn)}
                        disabled={rowDisabled}
                        onChange={event => handleToggleComplete(column, event.target.checked)}
                      />
                    }
                  />

                  <div className='flex items-center'>
                    {isEditing ? (
                      <>
                        <IconButton
                          size='small'
                          color='primary'
                          disabled={isBusy}
                          onClick={() => handleRenameSave(column)}
                          aria-label={`Save name for ${column.name}`}
                        >
                          <i className='tabler-check' />
                        </IconButton>
                        <IconButton
                          size='small'
                          disabled={isBusy}
                          onClick={() => setEditingId(null)}
                          aria-label={`Cancel renaming ${column.name}`}
                        >
                          <i className='tabler-x' />
                        </IconButton>
                      </>
                    ) : (
                      <>
                        <IconButton
                          size='small'
                          disabled={rowDisabled || index === 0}
                          onClick={() => handleMove(index, -1)}
                          aria-label={`Move ${column.name} left`}
                        >
                          <i className='tabler-arrow-up' />
                        </IconButton>
                        <IconButton
                          size='small'
                          disabled={rowDisabled || index === columns.length - 1}
                          onClick={() => handleMove(index, 1)}
                          aria-label={`Move ${column.name} right`}
                        >
                          <i className='tabler-arrow-down' />
                        </IconButton>
                        <IconButton
                          size='small'
                          color='primary'
                          disabled={rowDisabled}
                          onClick={() => handleRenameStart(column)}
                          aria-label={`Rename ${column.name}`}
                        >
                          <i className='tabler-edit' />
                        </IconButton>
                        <IconButton
                          size='small'
                          color='error'
                          disabled={rowDisabled}
                          onClick={() => setColumnToDelete(column)}
                          aria-label={`Delete ${column.name}`}
                        >
                          <i className='tabler-trash' />
                        </IconButton>
                      </>
                    )}
                  </div>
                </div>
              )
            })}

            <div className='flex flex-wrap items-end gap-3'>
              <CustomTextField
                className='flex-1 min-is-[200px]'
                label='New column'
                placeholder='e.g. QA Review'
                value={newName}
                onChange={event => setNewName(event.target.value)}
                onKeyDown={event => {
                  if (event.key === 'Enter') handleAdd()
                }}
              />
              <Button
                variant='tonal'
                startIcon={<i className='tabler-plus' />}
                disabled={!newName.trim() || isBusy || !projectKey}
                onClick={handleAdd}
              >
                Add column
              </Button>
            </div>
          </div>
        </DialogContent>
        <DialogActions>
          <Button variant='contained' onClick={onClose}>
            Done
          </Button>
        </DialogActions>
      </Dialog>

      <ConfirmDialog
        open={Boolean(columnToDelete)}
        title='Delete board column'
        description={`This removes the "${columnToDelete?.name ?? ''}" column from every board of this project. Cards in it fall back to the first column.`}
        confirmLabel='Delete'
        onClose={() => setColumnToDelete(null)}
        onConfirm={() => removeMutation.mutate(columnToDelete.id, { onSuccess: () => setColumnToDelete(null) })}
        loading={removeMutation.isPending}
      />
    </>
  )
}

export default BoardColumnManager
