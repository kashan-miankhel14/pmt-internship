'use client'

// React
import { useEffect, useMemo } from 'react'

// Next
import dynamic from 'next/dynamic'

// MUI components
import Box from '@mui/material/Box'
import Card from '@mui/material/Card'
import CardContent from '@mui/material/CardContent'
import Grid from '@mui/material/Grid'
import Typography from '@mui/material/Typography'

// Hooks
import { useDashboard } from '@/hooks/useDashboard'

// Components
import MetricCard from '@/components/pmt/MetricCard'
import PageHeader from '@/components/pmt/PageHeader'
import Logo from '@components/layout/shared/Logo'

const CHART_HEIGHT = 320

// Stable identity for "no report data yet", so the memos below are not invalidated on every
// render while the query is in flight.
const EMPTY_SERIES = []

/*
  apexcharts is by far the largest dependency in the app and /home is the route users land on
  after login, so a static import put the whole charting engine on the critical path of the
  first authenticated page. Loading it on demand (client-only: the chart has nothing to render
  on the server anyway) lets the metric cards paint and hydrate first, with the charts filling
  their reserved boxes a moment later. The placeholder keeps the same height, so nothing shifts.
*/
const Chart = dynamic(() => import('react-apexcharts'), {
  ssr: false,
  loading: () => <Box sx={{ blockSize: CHART_HEIGHT }} />
})

const Dashboard = () => {
  const { data } = useDashboard()

  const velocity = data?.velocity ?? EMPTY_SERIES
  const workload = data?.workload ?? EMPTY_SERIES

  // The chart wrapper deep-compares `series` and `options` on every render and re-applies the
  // chart when they differ. Rebuilding these literals on each render made that comparison walk
  // the full option tree for nothing; they only ever change when the report data changes.
  const velocitySeries = useMemo(
    () => [
      { name: 'Stories', type: 'column', data: velocity.map(item => item.completedStories) },
      { name: 'Tasks', type: 'column', data: velocity.map(item => item.completedTasks) },
      { name: 'Story points', type: 'line', data: velocity.map(item => item.completedStoryPoints) }
    ],
    [velocity]
  )

  const workloadSeries = useMemo(
    () => [
      { name: 'Open tasks', data: workload.map(member => member.openTasks) },
      { name: 'Open issues', data: workload.map(member => member.openIssues) },
      { name: 'Estimated hours', data: workload.map(member => member.estimatedHours) }
    ],
    [workload]
  )

  const velocityOptions = useMemo(
    () => ({
      chart: {
        type: 'line',
        fontFamily: 'inherit',
        toolbar: { show: false },
        zoom: { enabled: false },
        animations: { enabled: true, easing: 'easeinout', speed: 600 }
      },
      colors: ['var(--mui-palette-primary-main)', 'var(--mui-palette-success-main)', 'var(--mui-palette-primary-main)'],
      stroke: { width: [0, 0, 3], curve: 'smooth' },
      plotOptions: { bar: { columnWidth: '55%', borderRadius: 4 } },
      dataLabels: { enabled: false },
      grid: { borderColor: 'var(--mui-palette-divider)', strokeDashArray: 4, padding: { left: 8, right: 8 } },
      legend: {
        position: 'top',
        horizontalAlign: 'left',
        markers: { radius: 4 },
        labels: { colors: 'var(--mui-palette-text-secondary)' }
      },
      xaxis: {
        categories: velocity.map(item => item.period),
        labels: { style: { colors: 'var(--mui-palette-text-secondary)' } },
        axisBorder: { color: 'var(--mui-palette-divider)' },
        axisTicks: { color: 'var(--mui-palette-divider)' }
      },
      yaxis: [
        {
          seriesName: 'Stories',
          title: { text: 'Completed', style: { color: 'var(--mui-palette-text-secondary)', fontWeight: 500 } },
          labels: { style: { colors: 'var(--mui-palette-text-secondary)' } }
        },
        { seriesName: 'Stories', show: false },
        {
          opposite: true,
          seriesName: 'Story points',
          title: { text: 'Story points', style: { color: 'var(--mui-palette-text-secondary)', fontWeight: 500 } },
          labels: { style: { colors: 'var(--mui-palette-text-secondary)' } }
        }
      ],
      tooltip: { shared: true, intersect: false },
      noData: { text: 'No velocity data', style: { color: 'var(--mui-palette-text-secondary)' } }
    }),
    [velocity]
  )

  const workloadOptions = useMemo(
    () => ({
      chart: {
        type: 'bar',
        fontFamily: 'inherit',
        toolbar: { show: false },
        zoom: { enabled: false }
      },
      colors: ['var(--mui-palette-info-main)', 'var(--mui-palette-warning-main)', 'var(--mui-palette-primary-main)'],
      plotOptions: { bar: { horizontal: true, borderRadius: 4, barHeight: '60%' } },
      dataLabels: { enabled: false },
      grid: { borderColor: 'var(--mui-palette-divider)', strokeDashArray: 4 },
      legend: {
        position: 'top',
        horizontalAlign: 'left',
        markers: { radius: 4 },
        labels: { colors: 'var(--mui-palette-text-secondary)' }
      },
      xaxis: {
        categories: workload.map(member => member.userName),
        labels: { style: { colors: 'var(--mui-palette-text-secondary)' } },
        axisBorder: { color: 'var(--mui-palette-divider)' },
        axisTicks: { color: 'var(--mui-palette-divider)' }
      },
      tooltip: { shared: true, intersect: false },
      noData: { text: 'No workload data', style: { color: 'var(--mui-palette-text-secondary)' } }
    }),
    [workload]
  )

  useEffect(() => {
    // Keep a single ApexCharts reference available for the wrapper on the client. Imported on
    // demand so the constructor rides the chart's async chunk instead of the page bundle.
    if (typeof window === 'undefined' || window.ApexCharts) return

    import('apexcharts').then(module => {
      window.ApexCharts = module.default
    })
  }, [])

  return (
    <div className='flex flex-col gap-6 animate-slide-up'>
      <Card className='rounded-2xl border relative overflow-hidden animate-scale-in' style={{ background: 'linear-gradient(135deg, #142D4C 0%, #285586 100%)', color: '#fff', border: 'none' }}>
        {/* Animated background graphic grid */}
        <div className='absolute inset-0 opacity-10' style={{ backgroundImage: 'radial-gradient(circle at 1px 1px, white 1px, transparent 0)', backgroundSize: '16px 16px' }} />
        
        {/* Floating animated ambient light */}
        <div className='absolute -right-20 -top-20 w-80 h-80 rounded-full blur-3xl opacity-20 bg-[#66AAFF] animate-pulse' style={{ animationDuration: '6s' }} />

        <CardContent className='flex flex-col md:flex-row md:items-center justify-between gap-6 p-8 relative z-10' style={{ paddingBottom: '32px' }}>
          <div className='flex flex-col gap-2'>
            <Typography variant='caption' sx={{ textTransform: 'uppercase', letterSpacing: '1.5px', fontWeight: 700, opacity: 0.9, color: '#9cc9f2' }}>
              Delivery Cockpit
            </Typography>
            <Typography variant='h3' sx={{ fontWeight: 800, color: '#ffffff', letterSpacing: '-0.5px' }}>
              Welcome to Zenith
            </Typography>
            <Typography variant='body1' sx={{ color: '#e8f1fa', opacity: 0.95, maxWidth: '550px', fontSize: '1.05rem', lineHeight: 1.6 }}>
              Your high-performance workspace is up-to-date. Track velocity, manage tasks, solve issues, and check team workload live from the cockpit.
            </Typography>
          </div>
          
          {/* Stunning glowing logo element */}
          <div className='flex items-center justify-center bg-white/10 backdrop-blur-md rounded-3xl p-6 border border-white/20 hover:scale-105 transition-transform duration-300 shadow-lg shadow-black/10'>
            <Logo className='text-6xl text-white' />
          </div>
        </CardContent>
      </Card>

      <Grid container spacing={6} className='animate-scale-in' style={{ animationDelay: '100ms' }}>
        <Grid size={{ xs: 12, sm: 6, xl: 3 }}>
          <MetricCard label='Projects' value={data?.totals?.projects ?? '--'} helper='Total records' icon='tabler-layout-grid' tone='primary' />
        </Grid>
        <Grid size={{ xs: 12, sm: 6, xl: 3 }}>
          <MetricCard label='Tasks' value={data?.totals?.tasks ?? '--'} helper='Total records' icon='tabler-checklist' tone='info' />
        </Grid>
        <Grid size={{ xs: 12, sm: 6, xl: 3 }}>
          <MetricCard label='Issues' value={data?.totals?.issues ?? '--'} helper='Total records' icon='tabler-bug' tone='warning' />
        </Grid>
        <Grid size={{ xs: 12, sm: 6, xl: 3 }}>
          <MetricCard label='Departments' value={data?.totals?.departments ?? '--'} helper='Total records' icon='tabler-building-community' tone='success' />
        </Grid>
      </Grid>

      <Grid container spacing={6} className='animate-slide-up' style={{ animationDelay: '250ms' }}>
        <Grid size={{ xs: 12, xl: 7 }}>
          <Card className='rounded-xl border shadow-sm p-4 hover-elevate'>
            <CardContent className='flex h-full flex-col gap-4' sx={{ p: 0 }}>
              <div>
                <Typography variant='h6'>Velocity</Typography>
                <Typography variant='body2' color='text.secondary'>
                  Completed stories and tasks per month over the last six months, with story points on a secondary axis.
                </Typography>
              </div>
              <Chart type='line' height={CHART_HEIGHT} series={velocitySeries} options={velocityOptions} />
            </CardContent>
          </Card>
        </Grid>
        <Grid size={{ xs: 12, xl: 5 }}>
          <Card className='rounded-xl border shadow-sm p-4 hover-elevate'>
            <CardContent className='flex h-full flex-col gap-4' sx={{ p: 0 }}>
              <div>
                <Typography variant='h6'>Team workload (hours)</Typography>
                <Typography variant='body2' color='text.secondary'>
                  Open tasks, open issues, and estimated hours split by team member.
                </Typography>
              </div>
              <Chart type='bar' height={CHART_HEIGHT} series={workloadSeries} options={workloadOptions} />
            </CardContent>
          </Card>
        </Grid>
      </Grid>
    </div>
  )
}

export default Dashboard
