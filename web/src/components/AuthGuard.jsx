'use client'

import { useEffect } from 'react'

import { usePathname, useRouter } from 'next/navigation'

import Typography from '@mui/material/Typography'
import CircularProgress from '@mui/material/CircularProgress'
import Logo from '@components/layout/shared/Logo'

import { useAuth } from '@/contexts/AuthContext'
import { useAbility } from '@/contexts/AbilityContext'
import verticalMenuData from '@/data/navigation/verticalMenuData'

const AuthGuard = ({ children }) => {
  const router = useRouter()
  const pathname = usePathname()
  const { ready, session } = useAuth()
  const ability = useAbility()

  useEffect(() => {
    if (ready && !session) router.replace(`/login?next=${encodeURIComponent(pathname)}`)
  }, [pathname, ready, router, session])

  if (!ready || !session) {
    return (
      <div className='flex min-bs-[100vh] flex-col items-center justify-center bg-[#f5f7fb] animate-fade-in'>
        <div className='flex flex-col items-center gap-5'>
          {/* Centered glowing Zenith Z Logo */}
          <div className='pulse-indicator rounded-3xl p-6 bg-white border border-[#d6dce6] shadow-md flex items-center justify-center hover:scale-105 transition-transform duration-300'>
            <Logo className='text-6xl text-[#142D4C]' />
          </div>
          <Typography variant='h6' sx={{ fontWeight: 600, color: '#142D4C' }} className='animate-pulse'>
            Initializing Zenith Workspace...
          </Typography>
          <CircularProgress size={24} sx={{ color: '#142D4C' }} />
        </div>
      </div>
    )
  }

  // Permission check - ensure ability is loaded before checking
  if (ability.rules) {
    const menuData = verticalMenuData()

    const requiredPerm = menuData.find(m => pathname.startsWith(m.href))

    if (requiredPerm && requiredPerm.action && requiredPerm.subject && !ability.can(requiredPerm.action, requiredPerm.subject)) {
      router.replace('/forbidden')

      return null
    }
  }

  return children
}

export default AuthGuard
