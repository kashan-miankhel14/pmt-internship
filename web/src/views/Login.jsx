'use client'

// React Imports
import { useEffect, useState } from 'react'

// Next Imports
import { useRouter, useSearchParams } from 'next/navigation'

// MUI Imports
import useMediaQuery from '@mui/material/useMediaQuery'
import { styled, useTheme } from '@mui/material/styles'
import Typography from '@mui/material/Typography'
import IconButton from '@mui/material/IconButton'
import InputAdornment from '@mui/material/InputAdornment'
import Button from '@mui/material/Button'
import Alert from '@mui/material/Alert'
import Fade from '@mui/material/Fade'

// Third-party Imports
import classnames from 'classnames'
import { toast } from 'react-toastify'

// Component Imports
import Link from '@components/Link'
import Logo from '@components/layout/shared/Logo'
import CustomTextField from '@core/components/mui/TextField'

// Config Imports
import themeConfig from '@configs/themeConfig'

// Hook Imports
import { useSettings } from '@core/hooks/useSettings'

import { extractErrors } from '@/libs/errors'
import { sanitizeNextPath } from '@/libs/navigation'
import { useAuth } from '@/contexts/AuthContext'

const LoginV2 = () => {
  // States
  const [isPasswordShown, setIsPasswordShown] = useState(false)
  const [userNameOrEmail, setUserNameOrEmail] = useState('')
  const [password, setPassword] = useState('')
  const [errors, setErrors] = useState([])
  const [loading, setLoading] = useState(false)

  // Fade the sign-in form in on mount.
  const [showForm, setShowForm] = useState(false)

  useEffect(() => {
    setShowForm(true)
  }, [])

  // Hooks
  const router = useRouter()
  const searchParams = useSearchParams()
  const { settings } = useSettings()
  const theme = useTheme()
  const { login, session, ready } = useAuth()
  const hidden = useMediaQuery(theme.breakpoints.down('md'))

  const handleClickShowPassword = () => setIsPasswordShown(show => !show)

  const handleSubmit = async event => {
    event.preventDefault()
    setLoading(true)
    setErrors([])

    try {
      const session = await login({ userNameOrEmail, password })
      const next = sanitizeNextPath(searchParams.get('next'))
      router.push(next)
      toast.success(`Welcome back, ${session.displayName ?? session.userName}`)
    } catch (error) {
      const nextErrors =
        error.response?.status === 429
          ? ['Too many sign-in attempts. Please try again in a few minutes.']
          : extractErrors(error.response?.data ?? { errors: [error.message] })

      setErrors(nextErrors)
      toast.error(nextErrors[0])
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    if (ready && session) {
      const next = sanitizeNextPath(searchParams.get('next'))
      router.replace(next)
    }
  }, [ready, router, session, searchParams])

  return (
    <div className='flex bs-full justify-center'>
      {/* Left split pane with animated motion graphics */}
      <div
        className={classnames(
          'flex bs-full items-center justify-center flex-1 min-bs-[100dvh] relative p-6 max-md:hidden bg-[#f5f7fb]',
          {
            'border-ie': settings.skin === 'bordered'
          }
        )}
      >
        <div className='flex flex-col items-center justify-center gap-8 z-10 w-full max-w-[600px] text-center px-6 animate-scale-in'>
          {/* Animated Glowing Geometric Banner */}
          <div className='relative w-full aspect-square max-h-[380px] bg-gradient-to-br from-[#142D4C]/10 to-[#285586]/10 backdrop-blur-lg rounded-3xl border border-[#142D4C]/15 shadow-lg flex items-center justify-center overflow-hidden'>
            {/* Animated floating particles */}
            <div className='absolute inset-0 bg-[radial-gradient(circle_at_50%_120%,rgba(50,109,163,0.15),transparent_60%)]' />
            
            {/* Stunning vector geometric grid */}
            <svg width="280" height="280" viewBox="0 0 100 100" fill="none" className="absolute animate-spin-slow">
              <circle cx="50" cy="50" r="40" stroke="#142D4C" strokeWidth="0.25" strokeDasharray="2 2" opacity="0.3" />
              <circle cx="50" cy="50" r="30" stroke="#326DA3" strokeWidth="0.25" opacity="0.2" />
              <line x1="10" y1="50" x2="90" y2="50" stroke="#142D4C" strokeWidth="0.1" opacity="0.15" />
              <line x1="50" y1="10" x2="50" y2="90" stroke="#142D4C" strokeWidth="0.1" opacity="0.15" />
            </svg>
            
            {/* Centered glowing Zenith Logo */}
            <div className='pulse-indicator bg-white rounded-3xl p-6 border shadow-xl z-10 hover:scale-105 transition-transform duration-300'>
              <Logo className='text-7xl text-[#142D4C]' />
            </div>
          </div>
          <div className='flex flex-col gap-2'>
            <Typography variant='h3' sx={{ fontWeight: 800, color: '#142D4C' }}>
              Plan, Sync, Excel.
            </Typography>
            <Typography variant='body1' sx={{ color: 'text.secondary', maxWidth: '460px' }}>
              The high-performance workspace designed to speed up developer workflows and team productivity.
            </Typography>
          </div>
        </div>
      </div>

      {/* Right split pane with Sign In Form */}
      <div className='flex justify-center items-center bs-full bg-backgroundPaper !min-is-full p-6 md:!min-is-[unset] md:p-12 md:is-[480px]'>
        <Link className='absolute block-start-5 sm:block-start-[33px] inline-start-6 sm:inline-start-[38px]'>
          <Logo />
        </Link>
        <div className='flex flex-col gap-6 is-full sm:is-auto md:is-full sm:max-is-[400px] md:max-is-[unset] mbs-11 sm:mbs-14 md:mbs-0'>
          <div className='flex flex-col gap-1'>
            <Typography variant='h4'>{`Welcome to ${themeConfig.templateName}`}</Typography>
            <Typography>Sign in to your Zenith workspace.</Typography>
          </div>
          <Fade in={showForm} timeout={400}>
            <form
              noValidate
              autoComplete='off'
              onSubmit={handleSubmit}
              className='flex flex-col gap-5'
            >
              {errors.length ? (
                <Alert severity='error'>
                  {errors.map(error => (
                    <div key={error}>{error}</div>
                  ))}
                </Alert>
              ) : null}
              <CustomTextField
                autoFocus
                fullWidth
                label='Email or Username'
                placeholder='Enter your email or username'
                value={userNameOrEmail}
                onChange={event => setUserNameOrEmail(event.target.value)}
              />
              <CustomTextField
                fullWidth
                label='Password'
                placeholder='············'
                id='outlined-adornment-password'
                type={isPasswordShown ? 'text' : 'password'}
                value={password}
                onChange={event => setPassword(event.target.value)}
                slotProps={{
                  input: {
                    endAdornment: (
                      <InputAdornment position='end'>
                        <IconButton edge='end' onClick={handleClickShowPassword} onMouseDown={e => e.preventDefault()}>
                          <i className={isPasswordShown ? 'tabler-eye-off' : 'tabler-eye'} />
                        </IconButton>
                      </InputAdornment>
                    )
                  }
                }}
              />
              <Button fullWidth variant='contained' type='submit' disabled={loading}>
                {loading ? 'Signing in...' : 'Login'}
              </Button>
            </form>
          </Fade>
        </div>
      </div>
    </div>
  )
}

export default LoginV2
