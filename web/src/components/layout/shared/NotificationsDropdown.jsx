'use client'

// React Imports
import { useEffect, useRef, useState } from 'react'

// Next Imports
import { useRouter } from 'next/navigation'

// MUI Imports
import Badge from '@mui/material/Badge'
import ClickAwayListener from '@mui/material/ClickAwayListener'
import Divider from '@mui/material/Divider'
import Fade from '@mui/material/Fade'
import IconButton from '@mui/material/IconButton'
import List from '@mui/material/List'
import ListItemButton from '@mui/material/ListItemButton'
import ListItemText from '@mui/material/ListItemText'
import ListItemAvatar from '@mui/material/ListItemAvatar'
import Paper from '@mui/material/Paper'
import Popper from '@mui/material/Popper'
import Typography from '@mui/material/Typography'
import Button from '@mui/material/Button'
import Tooltip from '@mui/material/Tooltip'

// Third-party Imports
import { toast } from 'react-toastify'

// Core Components
import CustomAvatar from '@core/components/mui/Avatar'

// Hook Imports
import { useQueryClient } from '@tanstack/react-query'

import { useNotifications } from '@/hooks/useNotifications'

const getNotificationMeta = type => {
  const t = String(type || '').toLowerCase()
  if (t.includes('completed') || t.includes('done')) {
    return { icon: 'tabler-circle-check', color: 'success' }
  }
  if (t.includes('assigned')) {
    return { icon: 'tabler-user-check', color: 'info' }
  }
  if (t.includes('sprint')) {
    return { icon: 'tabler-flag', color: 'primary' }
  }
  if (t.includes('issue') || t.includes('bug')) {
    return { icon: 'tabler-bug', color: 'error' }
  }
  return { icon: 'tabler-bell', color: 'secondary' }
}

const formatRelativeTime = dateStr => {
  if (!dateStr) return ''
  const d = new Date(dateStr)
  if (isNaN(d.getTime())) return ''
  const diffSec = Math.floor((Date.now() - d.getTime()) / 1000)
  if (diffSec < 60) return 'Just now'
  if (diffSec < 3600) return `${Math.floor(diffSec / 60)}m ago`
  if (diffSec < 86400) return `${Math.floor(diffSec / 3600)}h ago`
  return d.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })
}

const NotificationsDropdown = () => {
  const router = useRouter()
  const queryClient = useQueryClient()
  const anchorRef = useRef(null)
  const [open, setOpen] = useState(false)
  const { data, canRead, markReadMutation, markAllReadMutation } = useNotifications()
  const notifications = Array.isArray(data) ? data : []
  const unreadCount = notifications.filter(item => !item.isRead).length

  useEffect(() => {
    const handleRealtime = event => {
      const notification = event.detail

      if (!notification) return

      toast.info(notification.title || notification.type || 'New notification', {
        autoClose: 4000
      })

      queryClient.setQueryData(['notifications'], old => {
        const existing = old || []

        if (existing.some(n => n.id === notification.id)) return existing

        return [notification, ...existing]
      })
    }

    window.addEventListener('pmt:notification', handleRealtime)

    return () => window.removeEventListener('pmt:notification', handleRealtime)
  }, [queryClient])

  const handleNotification = notification => {
    if (!notification.isRead) markReadMutation.mutate(notification.id)
    setOpen(false)

    const link = notification?.link

    if (typeof link === 'string' && link.startsWith('/') && !link.startsWith('//')) router.push(link)
  }

  const handleMarkAllAsRead = async () => {
    const unreadIds = notifications.filter(item => !item.isRead).map(item => item.id)
    if (!unreadIds.length) return
    try {
      await markAllReadMutation.mutateAsync(unreadIds)
      toast.success('All notifications marked as read')
    } catch {
      toast.error('Failed to mark all notifications as read')
    }
  }

  if (!canRead) return null

  return (
    <>
      <IconButton ref={anchorRef} onClick={() => setOpen(value => !value)} className='mie-1'>
        <Badge badgeContent={unreadCount} color='error' max={9}>
          <i className='tabler-bell text-xl' />
        </Badge>
      </IconButton>
      <Popper
        open={open}
        anchorEl={anchorRef.current}
        placement='bottom-end'
        transition
        disablePortal
        className='z-[1200] mt-3 w-[380px] max-w-[calc(100vw-32px)]'
      >
        {({ TransitionProps }) => (
          <Fade {...TransitionProps}>
            <Paper className='shadow-xl overflow-hidden rounded-xl border border-[var(--mui-palette-divider)]'>
              <ClickAwayListener onClickAway={() => setOpen(false)}>
                <div>
                  <div className='flex items-center justify-between px-4 py-3 border-b border-[var(--mui-palette-divider)]'>
                    <div>
                      <Typography variant='subtitle1' className='font-semibold' color='text.primary'>
                        Notifications
                      </Typography>
                      <Typography variant='caption' color='text.secondary'>
                        {unreadCount > 0 ? `${unreadCount} unread` : 'All caught up'}
                      </Typography>
                    </div>
                    {unreadCount > 0 ? (
                      <Tooltip title='Mark all notifications as read'>
                        <Button
                          size='small'
                          variant='tonal'
                          color='primary'
                          className='text-xs font-semibold normal-case min-is-0 px-2.5 py-1'
                          startIcon={<i className='tabler-checks text-sm' />}
                          onClick={handleMarkAllAsRead}
                          disabled={markAllReadMutation.isPending}
                        >
                          Read all
                        </Button>
                      </Tooltip>
                    ) : null}
                  </div>
                  <Divider />
                  <List disablePadding className='max-bs-[380px] overflow-y-auto divide-y divide-[var(--mui-palette-divider)]'>
                    {notifications.map(notification => {
                      const meta = getNotificationMeta(notification.type)
                      const timeStr = formatRelativeTime(notification.insertDate || notification.createdAt)

                      return (
                        <ListItemButton
                          key={notification.id}
                          onClick={() => handleNotification(notification)}
                          className={`py-3 px-4 transition-colors ${!notification.isRead ? 'bg-actionHover' : ''}`}
                        >
                          <ListItemAvatar className='min-is-0 me-3'>
                            <CustomAvatar skin='light' color={meta.color} size={36}>
                              <i className={`${meta.icon} text-lg`} />
                            </CustomAvatar>
                          </ListItemAvatar>
                          <ListItemText
                            disableTypography
                            primary={
                              <div className='flex items-center justify-between gap-2'>
                                <Typography
                                  variant='body2'
                                  className='font-semibold line-clamp-1'
                                  color='text.primary'
                                >
                                  {notification.title || notification.type}
                                </Typography>
                                <Typography variant='caption' color='text.disabled' className='text-[11px] whitespace-nowrap'>
                                  {timeStr}
                                </Typography>
                              </div>
                            }
                            secondary={
                              <Typography
                                variant='caption'
                                color='text.secondary'
                                className='line-clamp-2 mt-0.5 block'
                              >
                                {notification.message}
                              </Typography>
                            }
                          />
                          {!notification.isRead && (
                            <span className='w-2 h-2 rounded-full bg-[var(--mui-palette-primary-main)] shrink-0 ms-2' />
                          )}
                        </ListItemButton>
                      )
                    })}
                    {!notifications.length ? (
                      <div className='flex flex-col items-center justify-center p-8 text-center'>
                        <i className='tabler-bell-off text-3xl mb-2 text-[var(--mui-palette-text-disabled)]' />
                        <Typography variant='body2' color='text.secondary'>
                          You have no notifications yet.
                        </Typography>
                      </div>
                    ) : null}
                  </List>
                </div>
              </ClickAwayListener>
            </Paper>
          </Fade>
        )}
      </Popper>
    </>
  )
}

export default NotificationsDropdown
