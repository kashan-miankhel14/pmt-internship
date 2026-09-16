'use client'

import { useEffect, useState } from 'react'

import IconButton from '@mui/material/IconButton'
import Tooltip from '@mui/material/Tooltip'

const HighContrastToggle = () => {
  const [isHighContrast, setIsHighContrast] = useState(false)

  useEffect(() => {
    const saved = localStorage.getItem('high-contrast') === 'true'
    setIsHighContrast(saved)
    if (saved) {
      document.documentElement.classList.add('high-contrast')
      document.body.classList.add('high-contrast')
    }
  }, [])

  const handleToggle = () => {
    const newValue = !isHighContrast
    setIsHighContrast(newValue)
    localStorage.setItem('high-contrast', String(newValue))
    if (newValue) {
      document.documentElement.classList.add('high-contrast')
      document.body.classList.add('high-contrast')
    } else {
      document.documentElement.classList.remove('high-contrast')
      document.body.classList.remove('high-contrast')
    }
  }

  return (
    <Tooltip title={isHighContrast ? 'Disable High Contrast' : 'Enable High Contrast'}>
      <IconButton onClick={handleToggle} className='text-textPrimary'>
        <i className={isHighContrast ? 'tabler-eye-off' : 'tabler-eye'} />
      </IconButton>
    </Tooltip>
  )
}

export default HighContrastToggle
