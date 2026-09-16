'use client'

import Dialog from '@mui/material/Dialog'
import DialogTitle from '@mui/material/DialogTitle'
import DialogContent from '@mui/material/DialogContent'
import DialogActions from '@mui/material/DialogActions'
import Button from '@mui/material/Button'
import Typography from '@mui/material/Typography'

// `confirmLabel` defaults to the soft-delete wording the CRUD modules use; screens that really
// remove a row (team members, project access grants) pass their own verb.
const ConfirmDialog = ({ open, title, description, onClose, onConfirm, loading, confirmLabel = 'Deactivate' }) => {
  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth='xs'>
      <DialogTitle>{title}</DialogTitle>
      <DialogContent>
        <Typography color='text.secondary'>{description}</Typography>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button color='error' variant='contained' onClick={onConfirm} disabled={loading}>
          {confirmLabel}
        </Button>
      </DialogActions>
    </Dialog>
  )
}

export default ConfirmDialog
