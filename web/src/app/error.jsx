'use client'

import { useEffect } from 'react'

import Button from '@mui/material/Button'

export default function Error({ error, reset }) {
  useEffect(() => {
    console.error('Global Error Boundary caught:', error)
  }, [error])

  return (
    <div className='flex flex-col items-center justify-center min-bs-[100dvh] p-6 text-center'>
      <h1 className='text-4xl font-bold mb-4'>Something went wrong</h1>
      <p className='text-muted-foreground mb-8'>An unexpected error occurred in the application.</p>
      <Button variant='contained' onClick={() => reset()}>
        Try Again
      </Button>
    </div>
  )
}
