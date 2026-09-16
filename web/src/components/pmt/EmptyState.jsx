'use client'

import { useEffect, useState } from 'react'

import Card from '@mui/material/Card'
import CardContent from '@mui/material/CardContent'
import Typography from '@mui/material/Typography'
import Grow from '@mui/material/Grow'

const EmptyState = ({ title, description, icon = 'tabler-folder-off' }) => {
  // Grow the card in from its centre on mount.
  const [show, setShow] = useState(false)

  useEffect(() => {
    setShow(true)
  }, [])

  return (
    <Grow in={show} timeout={400}>
      <Card>
      <CardContent className='flex min-bs-[220px] flex-col items-center justify-center gap-4 text-center'>
        <div className='flex bs-16 is-16 items-center justify-center rounded-full bg-actionHover text-textSecondary'>
          <i className={`${icon} text-[2rem]`} />
        </div>
        <div className='flex max-is-[420px] flex-col gap-2'>
          <Typography variant='h6'>{title}</Typography>
          <Typography color='text.secondary'>{description}</Typography>
        </div>
      </CardContent>
    </Card>
    </Grow>
  )
}

export default EmptyState
