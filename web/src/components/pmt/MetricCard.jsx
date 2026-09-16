'use client'

import Card from '@mui/material/Card'
import CardContent from '@mui/material/CardContent'
import Typography from '@mui/material/Typography'

const MetricCard = ({ label, value, helper, icon, tone = 'primary' }) => {
  return (
    <Card className='shadow-sm hover-elevate relative overflow-hidden' style={{ borderBottom: `3px solid var(--mui-palette-${tone}-main)` }}>
      <CardContent className='flex items-start justify-between gap-4 p-6'>
        <div className='flex flex-col gap-1'>
          <Typography variant='body2' sx={{ fontWeight: 500, letterSpacing: '0.2px' }} color='text.secondary'>
            {label}
          </Typography>
          <Typography variant='h4' sx={{ fontWeight: 700, mt: 1 }}>{value}</Typography>
          {helper ? <Typography variant='caption' color='text.secondary'>{helper}</Typography> : null}
        </div>
        <div
          className='flex bs-12 is-12 items-center justify-center rounded-2xl pulse-indicator'
          style={{
            backgroundColor: `var(--mui-palette-${tone}-lightOpacity)`,
            color: `var(--mui-palette-${tone}-main)`,
            border: `1px solid var(--mui-palette-${tone}-mainOpacity)`
          }}
        >
          <i className={`${icon} text-[1.6rem]`} />
        </div>
      </CardContent>
    </Card>
  )
}

export default MetricCard
