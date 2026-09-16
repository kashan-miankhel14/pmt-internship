'use client'

import { useEffect, useState } from 'react'

import Card from '@mui/material/Card'
import CardContent from '@mui/material/CardContent'
import Button from '@mui/material/Button'
import Typography from '@mui/material/Typography'
import Fade from '@mui/material/Fade'

/**
 * `actions` renders extra controls (links, secondary buttons) beside the primary action, and
 * `actionIcon` lets a screen replace the default "plus" glyph when the action is not a create.
 */
const PageHeader = ({
  eyebrow,
  title,
  description,
  actionLabel,
  onAction,
  actionDisabled,
  actionIcon = 'tabler-plus',
  actions
}) => {
  // Reveal on mount so the header eases in instead of snapping in.
  const [show, setShow] = useState(false)

  useEffect(() => {
    setShow(true)
  }, [])

  return (
    <Fade in={show} timeout={400}>
      <Card className='overflow-hidden'>
        <CardContent className='flex flex-col gap-2 p-3.5 sm:flex-row sm:items-center sm:justify-between'>
          <div className='flex max-is-[720px] flex-col gap-0.5'>
            {eyebrow ? (
              <Typography variant='overline' className='tracking-[0.16em] text-primary text-[10px] leading-tight'>
                {eyebrow}
              </Typography>
            ) : null}
            <Typography variant='h5' className='font-bold'>{title}</Typography>
            <Typography variant='body2' color='text.secondary' className='text-xs'>{description}</Typography>
          </div>
          {actionLabel || actions ? (
            <div className='flex flex-wrap items-center gap-2 mt-1 sm:mt-0'>
              {actions}
              {actionLabel ? (
                <Button
                  size='small'
                  variant='contained'
                  onClick={onAction}
                  disabled={actionDisabled}
                  startIcon={<i className={actionIcon} />}
                  className='font-semibold text-xs py-1 px-3'
                >
                  {actionLabel}
                </Button>
              ) : null}
            </div>
          ) : null}
        </CardContent>
    </Card>
    </Fade>
  )
}

export default PageHeader
