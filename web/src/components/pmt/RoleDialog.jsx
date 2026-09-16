'use client'

import { useEffect, useState } from 'react'

// MUI Imports
import Dialog from '@mui/material/Dialog'
import DialogTitle from '@mui/material/DialogTitle'
import DialogContent from '@mui/material/DialogContent'
import DialogActions from '@mui/material/DialogActions'
import FormGroup from '@mui/material/FormGroup'
import FormControlLabel from '@mui/material/FormControlLabel'
import Checkbox from '@mui/material/Checkbox'
import Button from '@mui/material/Button'
import Typography from '@mui/material/Typography'
import CircularProgress from '@mui/material/CircularProgress'

// Third-party Imports
import { toast } from 'react-toastify'

// Component Imports
import { usersService } from '@/services/users'

const RoleDialog = ({ open, userId, userName, onClose }) => {
  const [roles, setRoles] = useState([])
  const [selected, setSelected] = useState([])
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState(null)

  useEffect(() => {
    if (!open || !userId) return

    let active = true

    const load = async () => {
      setLoading(true)
      setError(null)

      try {
        const [available, assigned] = await Promise.all([usersService.availableRoles(), usersService.getRoles(userId)])

        if (!active) return

        // Both endpoints return RoleDto[]; the assignment is the subset already granted.
        setRoles(available ?? [])
        setSelected((assigned ?? []).map(role => role.id))
      } catch {
        if (active) setError('Failed to load roles. Please try again.')
      } finally {
        if (active) setLoading(false)
      }
    }

    load()

    return () => {
      active = false
    }
  }, [open, userId])

  const toggleRole = roleId => {
    setSelected(prev => (prev.includes(roleId) ? prev.filter(item => item !== roleId) : [...prev, roleId]))
  }

  const handleSave = async () => {
    setSaving(true)

    try {
      await usersService.setRoles(userId, selected)
      toast.success(`Roles updated for ${userName ?? 'user'}.`)
      onClose?.()
    } catch {
      toast.error('Could not save role assignments.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth='xs'>
      <DialogTitle>{userName ? `Assign roles · ${userName}` : 'Assign roles'}</DialogTitle>
      <DialogContent>
        {loading ? (
          <div className='flex justify-center p-4'>
            <CircularProgress size={24} />
          </div>
        ) : error ? (
          <Typography color='error'>{error}</Typography>
        ) : (
          <FormGroup className='mt-2'>
            {roles.map(role => (
              <FormControlLabel
                key={role.id}
                control={
                  <Checkbox checked={selected.includes(role.id)} onChange={() => toggleRole(role.id)} disabled={saving} />
                }
                label={role.name}
              />
            ))}
          </FormGroup>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={saving}>
          Cancel
        </Button>
        <Button variant='contained' onClick={handleSave} disabled={loading || saving}>
          Save
        </Button>
      </DialogActions>
    </Dialog>
  )
}

export default RoleDialog
