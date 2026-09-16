'use client'

import { useState } from 'react'

import Card from '@mui/material/Card'
import CardContent from '@mui/material/CardContent'
import Typography from '@mui/material/Typography'
import Button from '@mui/material/Button'
import Divider from '@mui/material/Divider'
import IconButton from '@mui/material/IconButton'
import CircularProgress from '@mui/material/CircularProgress'

// Third-party Imports
import { toast } from 'react-toastify'

import CustomTextField from '@core/components/mui/TextField'

// Component Imports
import EmptyState from '@/components/pmt/EmptyState'
import { errorMessage } from '@/components/pmt/ErrorState'

// Hook Imports
import { useComments } from '@/hooks/useComments'
import { useLookups } from '@/hooks/useLookups'
import { useAuth } from '@/contexts/AuthContext'
import { extractErrors } from '@/libs/errors'
import { isCommentable } from '@/services/comments'

const CommentsPanel = ({ entityType, entityId, embedded = false }) => {
  const [body, setBody] = useState('')
  const [editingId, setEditingId] = useState(null)
  const [editingBody, setEditingBody] = useState('')
  const supported = isCommentable(entityType)
  const { session } = useAuth()

  const { data, isLoading, isError, error, canManage, createMutation, updateMutation, deleteMutation } = useComments({
    entityType,
    entityId,
    enabled: supported
  })

  const { data: lookups = {} } = useLookups()

  const comments = Array.isArray(data) ? data : []

  if (!supported) {
    return (
      <EmptyState
        title='Comments are not available here'
        description={`The API does not support comments for this ${String(entityType).toLowerCase()}.`}
        icon='tabler-message-off'
      />
    )
  }

  const authorName = userId => lookups.users?.find(user => user.id === userId)?.displayName ?? 'Team member'

  const submit = async () => {
    if (!body.trim()) return

    try {
      await createMutation.mutateAsync({ entityType, entityId, body: body.trim() })
      setBody('')
    } catch (submitError) {
      toast.error(extractErrors(submitError.response?.data)[0])
    }
  }

  const remove = async id => {
    try {
      await deleteMutation.mutateAsync(id)
    } catch (deleteError) {
      toast.error(extractErrors(deleteError.response?.data)[0])
    }
  }

  const saveEdit = async id => {
    try {
      await updateMutation.mutateAsync({ id, body: editingBody.trim() })
      setEditingId(null)
      setEditingBody('')
    } catch (updateError) {
      toast.error(extractErrors(updateError.response?.data)[0])
    }
  }

  const content = (
    <CardContent className='flex flex-col gap-4'>
      <div>
        <Typography variant='h6'>Comments</Typography>
        <Typography variant='body2' color='text.secondary'>
          Shared thread for this {String(entityType).toLowerCase()}.
        </Typography>
      </div>
      {canManage ? (
        <>
          <CustomTextField
            multiline
            minRows={embedded ? 2 : 4}
            value={body}
            onChange={event => setBody(event.target.value)}
            placeholder='Write a comment for the team...'
          />
          <div className='flex justify-end'>
            <Button variant='contained' onClick={submit} disabled={createMutation.isPending || !body.trim()}>
              Add comment
            </Button>
          </div>
        </>
      ) : (
        <Typography variant='body2' color='text.secondary'>
          You do not have permission to read or post comments.
        </Typography>
      )}
      <Divider />
      {!canManage ? null : isLoading ? (
        <div className='flex justify-center p-4'>
          <CircularProgress size={24} />
        </div>
      ) : isError ? (
        <Typography variant='body2' color='error'>
          {errorMessage(error)}
        </Typography>
      ) : comments.length ? (
        <div className='flex flex-col gap-4'>
          {comments.map(comment => (
            <div key={comment.id} className='flex items-start justify-between gap-4 rounded-lg border p-4'>
              <div className='flex flex-col gap-1'>
                <Typography className='font-medium'>{authorName(comment.userId)}</Typography>
                <Typography variant='caption' color='text.secondary'>
                  {comment.insertDate ? new Date(comment.insertDate).toLocaleString() : 'Unknown date'}
                </Typography>
                 {editingId === comment.id ? (
                   <div className='flex flex-col gap-2'>
                     <CustomTextField value={editingBody} onChange={event => setEditingBody(event.target.value)} multiline minRows={2} />
                     <div className='flex gap-2'>
                       <Button size='small' variant='contained' onClick={() => saveEdit(comment.id)} disabled={updateMutation.isPending || !editingBody.trim()}>
                         Save
                       </Button>
                       <Button size='small' onClick={() => setEditingId(null)}>Cancel</Button>
                     </div>
                   </div>
                 ) : (
                   <Typography color='text.secondary'>{comment.body}</Typography>
                 )}
               </div>
               {editingId !== comment.id ? (
                 <div className='flex'>
                   {comment.userId === session?.userId ? (
                     <IconButton color='primary' onClick={() => { setEditingId(comment.id); setEditingBody(comment.body) }} aria-label='Edit comment'>
                       <i className='tabler-edit' />
                     </IconButton>
                   ) : null}
                   <IconButton color='error' onClick={() => remove(comment.id)} disabled={deleteMutation.isPending} aria-label='Delete comment'>
                     <i className='tabler-trash' />
                   </IconButton>
                 </div>
               ) : null}
            </div>
          ))}
        </div>
      ) : (
        <Typography variant='body2' color='text.secondary'>
          No comments yet. Start the thread above.
        </Typography>
      )}
    </CardContent>
  )

  // Inside a dialog the surrounding paper already provides the elevation.
  return embedded ? <Card variant='outlined'>{content}</Card> : <Card>{content}</Card>
}

export default CommentsPanel
