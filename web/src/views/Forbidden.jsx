'use client'

import Link from 'next/link'

import Button from '@mui/material/Button'
import Typography from '@mui/material/Typography'

const Forbidden = () => {
  return (
    <div className='flex min-bs-[100dvh] items-center justify-center p-6'>
      <div className='flex max-is-[520px] flex-col items-center gap-4 text-center'>
        <div className='flex bs-20 is-20 items-center justify-center rounded-full bg-warning/10 text-warning'>
          <i className='tabler-lock-x text-[2.5rem]' />
        </div>
        <Typography variant='h3'>Not authorized</Typography>
        <Typography color='text.secondary'>
          You do not have the permission required to view this area. Contact an administrator if you need access.
        </Typography>
        <Button component={Link} href='/home' variant='contained'>
          Return to dashboard
        </Button>
      </div>
    </div>
  )
}

export default Forbidden
