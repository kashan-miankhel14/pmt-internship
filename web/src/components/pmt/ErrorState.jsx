'use client'

import { useEffect, useState } from 'react'

import Button from '@mui/material/Button'
import Card from '@mui/material/Card'
import CardContent from '@mui/material/CardContent'
import Typography from '@mui/material/Typography'
import Fade from '@mui/material/Fade'

import { extractErrors } from '@/libs/errors'

/**
 * Turns an axios/react-query rejection into one display line. ProblemDetails bodies are
 * normalised by extractErrors; a 403 is reported as a permission problem because the API
 * answers those with an empty body.
 */
export const errorMessage = error => {
  if (!error) return 'Something went wrong. Please try again.'

  if (error.response?.status === 403) return 'You do not have permission to view this data.'

  const data = error.response?.data ?? { title: error.message }

  return extractErrors(data)[0]
}

const ErrorState = ({
  title = 'Could not load this list',
  error,
  description,
  onRetry,
  icon = 'tabler-alert-triangle'
}) => {
  // Fade the error card in on mount so a failed load does not snap into view.
  const [show, setShow] = useState(false)

  useEffect(() => {
    setShow(true)
  }, [])

  return (
    <Fade in={show} timeout={400}>
      <Card>
      <CardContent className='flex min-bs-[220px] flex-col items-center justify-center gap-4 text-center'>
        <div className='flex bs-16 is-16 items-center justify-center rounded-full bg-errorLight text-error'>
          <i className={`${icon} text-[2rem]`} />
        </div>
        <div className='flex max-is-[480px] flex-col gap-2'>
          <Typography variant='h6'>{title}</Typography>
          <Typography color='text.secondary'>{description ?? errorMessage(error)}</Typography>
        </div>
        {onRetry ? (
          <Button variant='tonal' color='error' startIcon={<i className='tabler-refresh' />} onClick={onRetry}>
            Try again
          </Button>
        ) : null}
      </CardContent>
    </Card>
    </Fade>
  )
}

export default ErrorState
