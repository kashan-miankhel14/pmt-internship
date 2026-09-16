'use client'

import { useEffect } from 'react'

import Button from '@mui/material/Button'

/**
 * Top-level React error boundary.
 *
 * app/error.jsx already boundaries the route segments, but it cannot catch an error thrown from
 * the root layout itself (or from error.jsx). global-error is the last line of defence for those:
 * it replaces the whole document, so it must render its own <html>/<body> and cannot rely on the
 * root layout's global CSS — hence the inline styles here (MUI's Button still styles itself via
 * emotion). It only activates in production; in development the Next.js error overlay shows first.
 */
export default function GlobalError({ error, reset }) {
  useEffect(() => {
    // eslint-disable-next-line no-console
    console.error('Global (root) Error Boundary caught:', error)
  }, [error])

  return (
    <html lang='en'>
      <body>
        <div
          style={{
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            justifyContent: 'center',
            minHeight: '100dvh',
            padding: '1.5rem',
            textAlign: 'center'
          }}
        >
          <h1 style={{ fontSize: '2.25rem', fontWeight: 700, marginBottom: '1rem' }}>Something went wrong</h1>
          <p style={{ marginBottom: '2rem' }}>An unexpected error occurred in the application.</p>
          <Button variant='contained' onClick={() => reset()}>
            Try Again
          </Button>
        </div>
      </body>
    </html>
  )
}
