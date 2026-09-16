'use client'

// React Imports
import { useEffect, useState } from 'react'

// MUI Imports
import Button from '@mui/material/Button'
import Dialog from '@mui/material/Dialog'
import DialogActions from '@mui/material/DialogActions'
import DialogContent from '@mui/material/DialogContent'
import DialogTitle from '@mui/material/DialogTitle'
import FormControlLabel from '@mui/material/FormControlLabel'
import Grid from '@mui/material/Grid'
import Switch from '@mui/material/Switch'

// Third-party Imports
import { Controller, useForm } from 'react-hook-form'

// Component Imports
import CustomTextField from '@core/components/mui/TextField'
import UserPicker from '@/components/pmt/UserPicker'

// Hook Imports
import { useLookups } from '@/hooks/useLookups'

// Lib Imports
import { KEY_PATTERN, deriveKey } from '@/libs/projectTemplates'

const emptyTeam = { key: '', name: '', description: '', lead: null, active: true }

/**
 * Create/edit dialog for a team (stage 2). The lead is picked as a user record and flattened to
 * `leadUserId` on submit, because `Teams.LeadUserId` is NOT NULL: a team always has a lead, and
 * the API also seats that user as the first `TeamMember` with `TeamRole = 'Lead'`.
 */
const TeamFormDialog = ({ open, team, onClose, onSubmit, loading }) => {
  const { data: lookups } = useLookups()

  // A manually typed key must never be overwritten by the name-derived suggestion.
  const [keyEdited, setKeyEdited] = useState(false)

  const { control, handleSubmit, reset, setValue, getValues } = useForm({ defaultValues: emptyTeam })

  useEffect(() => {
    if (!open) return

    setKeyEdited(Boolean(team?.key))

    if (!team) {
      reset(emptyTeam)

      return
    }

    // The list payload carries the lead's id (and often the display name); anything missing is
    // resolved from the shared user lookup so the picker opens with a real record selected.
    const leadFromLookup = lookups?.users?.find(user => user.id === team.leadUserId)

    reset({
      key: team.key ?? '',
      name: team.name ?? '',
      description: team.description ?? '',
      lead: team.leadUserId
        ? (leadFromLookup ?? { id: team.leadUserId, displayName: team.leadUserName ?? `User #${team.leadUserId}` })
        : null,
      active: team.active ?? team.isActive ?? true
    })
  }, [open, team, lookups?.users, reset])

  const handleNameBlur = () => {
    if (keyEdited) return

    const suggestion = deriveKey(getValues('name'))

    if (suggestion) setValue('key', suggestion, { shouldValidate: true })
  }

  const submit = form =>
    onSubmit({
      key: form.key,
      name: form.name,
      description: form.description,
      leadUserId: form.lead?.id ?? null,
      active: form.active
    })

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth='sm'>
      <DialogTitle>{team ? 'Edit team' : 'Create team'}</DialogTitle>
      <DialogContent>
        <Grid container spacing={4} className='pt-1'>
          <Grid size={{ xs: 12, sm: 4 }}>
            <Controller
              name='key'
              control={control}
              rules={{
                required: 'Key is required.',
                pattern: { value: KEY_PATTERN, message: '2-10 chars, A-Z or 0-9, starting with a letter.' }
              }}
              render={({ field, fieldState }) => (
                <CustomTextField
                  {...field}
                  fullWidth
                  label='Key'
                  placeholder='PLAT'
                  value={field.value ?? ''}
                  onChange={event => {
                    setKeyEdited(true)
                    field.onChange(event.target.value.toUpperCase())
                  }}
                  error={Boolean(fieldState.error)}
                  helperText={fieldState.error?.message ?? 'Suggested from the team name.'}
                />
              )}
            />
          </Grid>
          <Grid size={{ xs: 12, sm: 8 }}>
            <Controller
              name='name'
              control={control}
              rules={{ required: 'Name is required.' }}
              render={({ field, fieldState }) => (
                <CustomTextField
                  {...field}
                  fullWidth
                  label='Name'
                  placeholder='Platform Engineering'
                  value={field.value ?? ''}
                  onBlur={() => {
                    field.onBlur()
                    handleNameBlur()
                  }}
                  error={Boolean(fieldState.error)}
                  helperText={fieldState.error?.message}
                />
              )}
            />
          </Grid>
          <Grid size={{ xs: 12 }}>
            <Controller
              name='description'
              control={control}
              render={({ field }) => (
                <CustomTextField
                  {...field}
                  fullWidth
                  multiline
                  minRows={3}
                  label='Description'
                  placeholder='What this team owns.'
                  value={field.value ?? ''}
                />
              )}
            />
          </Grid>
          <Grid size={{ xs: 12 }}>
            <Controller
              name='lead'
              control={control}
              rules={{ required: 'A team must have a lead.' }}
              render={({ field, fieldState }) => (
                <UserPicker
                  label='Team lead'
                  required
                  value={field.value}
                  onChange={field.onChange}
                  error={Boolean(fieldState.error)}
                  helperText={fieldState.error?.message ?? 'The lead is added to the team as its first member.'}
                />
              )}
            />
          </Grid>
          <Grid size={{ xs: 12 }}>
            <Controller
              name='active'
              control={control}
              render={({ field }) => (
                <FormControlLabel
                  control={
                    <Switch checked={Boolean(field.value)} onChange={event => field.onChange(event.target.checked)} />
                  }
                  label='Active'
                />
              )}
            />
          </Grid>
        </Grid>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={loading}>
          Cancel
        </Button>
        <Button variant='contained' onClick={handleSubmit(submit)} disabled={loading}>
          {team ? 'Save changes' : 'Create team'}
        </Button>
      </DialogActions>
    </Dialog>
  )
}

export default TeamFormDialog
