'use client'

import { useMemo } from 'react'
import dynamic from 'next/dynamic'

import Box from '@mui/material/Box'
import Card from '@mui/material/Card'
import CardContent from '@mui/material/CardContent'
import Typography from '@mui/material/Typography'
import Grid from '@mui/material/Grid'
import Chip from '@mui/material/Chip'
import Button from '@mui/material/Button'
import LinearProgress from '@mui/material/LinearProgress'

import { toast } from 'react-toastify'
import { useLookups } from '@/hooks/useLookups'
import { useSprints } from '@/hooks/useSprints'
import { exportProjectToExcel } from '@/libs/excelExport'

const CHART_HEIGHT = 300

const Chart = dynamic(() => import('react-apexcharts'), {
  ssr: false,
  loading: () => <Box sx={{ height: CHART_HEIGHT }} className='flex items-center justify-center text-gray-400'>Loading chart…</Box>
})

export const ProjectAnalyticsView = ({ projectId, projectKey, stories = [], tasks = [], issues = [] }) => {
  const { sprints = [] } = useSprints(projectKey)
  const { data: lookups = {} } = useLookups()

  const handleExport = () => {
    try {
      const proj = lookups.projects?.find(p => p.id === Number(projectId) || p.key === projectKey) || {
        id: projectId,
        key: projectKey,
        name: `Project ${projectKey || projectId}`
      }
      exportProjectToExcel({ project: proj, stories, tasks, issues, sprints }, lookups)
      toast.success('Project Excel report downloaded!')
    } catch (e) {
      console.error(e)
      toast.error('Failed to export Excel report.')
    }
  }

  const usersList = lookups.users ?? []
  const userMap = useMemo(() => {
    const map = {}
    usersList.forEach(u => {
      map[u.id] = u.displayName || u.email
    })
    return map
  }, [usersList])

  // 1. Status Distribution (Tasks)
  const taskStatusDistribution = useMemo(() => {
    const counts = { ToDo: 0, InProgress: 0, Review: 0, Done: 0 }
    tasks.forEach(t => {
      const s = String(t.status || '').toLowerCase()
      if (s.includes('done') || s.includes('complete')) counts.Done++
      else if (s.includes('review') || s.includes('qa')) counts.Review++
      else if (s.includes('progress') || s.includes('active')) counts.InProgress++
      else counts.ToDo++
    })
    return counts
  }, [tasks])

  const donutSeries = useMemo(() => [
    taskStatusDistribution.ToDo,
    taskStatusDistribution.InProgress,
    taskStatusDistribution.Review,
    taskStatusDistribution.Done
  ], [taskStatusDistribution])

  const donutOptions = useMemo(() => ({
    chart: { type: 'donut', fontFamily: 'inherit', animations: { enabled: true } },
    labels: ['To Do', 'In Progress', 'In Review', 'Done'],
    colors: ['#7367F0', '#FF9F43', '#00CFE8', '#28C76F'],
    legend: { position: 'bottom', labels: { colors: 'var(--mui-palette-text-secondary)' } },
    plotOptions: {
      pie: {
        donut: {
          size: '70%',
          labels: {
            show: true,
            total: {
              show: true,
              label: 'Total Tasks',
              formatter: () => tasks.length
            }
          }
        }
      }
    },
    dataLabels: { enabled: true, dropShadow: { enabled: false } },
    stroke: { width: 2, colors: ['var(--mui-palette-background-paper)'] }
  }), [tasks.length])

  // 2. Team Workload & Effort (Estimated vs Actual Hours by Member)
  const { workloadCategories, estHoursData, actHoursData } = useMemo(() => {
    const memberEffort = {}

    tasks.forEach(t => {
      const uid = t.assignedToUserId || t.assigneeUserId || 0
      const name = uid ? (userMap[uid] || `User #${uid}`) : 'Unassigned'
      if (!memberEffort[name]) memberEffort[name] = { est: 0, act: 0, taskCount: 0 }
      memberEffort[name].est += Number(t.estimatedHours || 0)
      memberEffort[name].act += Number(t.actualHours || 0)
      memberEffort[name].taskCount++
    })

    const categories = Object.keys(memberEffort)
    const est = categories.map(k => memberEffort[k].est)
    const act = categories.map(k => memberEffort[k].act)

    return {
      workloadCategories: categories.length ? categories : ['No Assignees'],
      estHoursData: est.length ? est : [0],
      actHoursData: act.length ? act : [0]
    }
  }, [tasks, userMap])

  const workloadSeries = useMemo(() => [
    { name: 'Estimated Hours', data: estHoursData },
    { name: 'Actual Hours Spent', data: actHoursData }
  ], [estHoursData, actHoursData])

  const workloadOptions = useMemo(() => ({
    chart: { type: 'bar', fontFamily: 'inherit', toolbar: { show: false } },
    colors: ['#7367F0', '#28C76F'],
    plotOptions: { bar: { horizontal: false, columnWidth: '50%', borderRadius: 4 } },
    dataLabels: { enabled: false },
    stroke: { show: true, width: 2, colors: ['transparent'] },
    xaxis: {
      categories: workloadCategories,
      labels: { style: { colors: 'var(--mui-palette-text-secondary)', fontSize: '11px' } }
    },
    yaxis: {
      title: { text: 'Hours (h)', style: { color: 'var(--mui-palette-text-secondary)' } },
      labels: { style: { colors: 'var(--mui-palette-text-secondary)' } }
    },
    legend: { position: 'top', horizontalAlign: 'right', labels: { colors: 'var(--mui-palette-text-secondary)' } },
    grid: { borderColor: 'var(--mui-palette-divider)', strokeDashArray: 4 }
  }), [workloadCategories])

  // 3. Sprint Story Points & Velocity Burndown
  const { sprintLabels, committedPoints, completedPoints } = useMemo(() => {
    if (!sprints.length) {
      return {
        sprintLabels: ['Backlog'],
        committedPoints: [stories.reduce((s, x) => s + Number(x.storyPoints || 0), 0)],
        completedPoints: [stories.filter(x => String(x.status).toLowerCase() === 'done').reduce((s, x) => s + Number(x.storyPoints || 0), 0)]
      }
    }

    const labels = []
    const committed = []
    const completed = []

    sprints.forEach(s => {
      labels.push(s.name)
      const sStories = stories.filter(st => st.sprintId === s.id)
      const comm = sStories.reduce((sum, st) => sum + Number(st.storyPoints || 0), 0)
      const comp = sStories.filter(st => String(st.status).toLowerCase() === 'done').reduce((sum, st) => sum + Number(st.storyPoints || 0), 0)
      committed.push(comm)
      completed.push(comp)
    })

    return { sprintLabels: labels, committedPoints: committed, completedPoints: completed }
  }, [sprints, stories])

  const velocitySeries = useMemo(() => [
    { name: 'Committed Points', type: 'column', data: committedPoints },
    { name: 'Completed Points', type: 'line', data: completedPoints }
  ], [committedPoints, completedPoints])

  const velocityOptions = useMemo(() => ({
    chart: { type: 'line', fontFamily: 'inherit', toolbar: { show: false } },
    stroke: { width: [0, 3], curve: 'smooth' },
    colors: ['#7367F0', '#28C76F'],
    plotOptions: { bar: { columnWidth: '45%', borderRadius: 4 } },
    xaxis: {
      categories: sprintLabels,
      labels: { style: { colors: 'var(--mui-palette-text-secondary)' } }
    },
    yaxis: {
      title: { text: 'Story Points', style: { color: 'var(--mui-palette-text-secondary)' } },
      labels: { style: { colors: 'var(--mui-palette-text-secondary)' } }
    },
    legend: { position: 'top', horizontalAlign: 'left', labels: { colors: 'var(--mui-palette-text-secondary)' } },
    grid: { borderColor: 'var(--mui-palette-divider)', strokeDashArray: 4 }
  }), [sprintLabels])

  // 4. Issue Severity & Defect Distribution
  const severityCounts = useMemo(() => {
    const counts = { Critical: 0, High: 0, Medium: 0, Low: 0 }
    issues.forEach(iss => {
      const sev = iss.severity || 'Medium'
      if (counts[sev] !== undefined) counts[sev]++
      else counts.Medium++
    })
    return counts
  }, [issues])

  const severitySeries = useMemo(() => [
    { name: 'Reported Issues', data: [severityCounts.Critical, severityCounts.High, severityCounts.Medium, severityCounts.Low] }
  ], [severityCounts])

  const severityOptions = useMemo(() => ({
    chart: { type: 'bar', fontFamily: 'inherit', toolbar: { show: false } },
    colors: ['#EA5455', '#FF9F43', '#FFC107', '#28C76F'],
    plotOptions: {
      bar: {
        distributed: true,
        borderRadius: 4,
        horizontal: true,
        barHeight: '55%'
      }
    },
    dataLabels: { enabled: true },
    xaxis: {
      categories: ['Critical', 'High', 'Medium', 'Low'],
      labels: { style: { colors: 'var(--mui-palette-text-secondary)' } }
    },
    yaxis: { labels: { style: { colors: 'var(--mui-palette-text-secondary)' } } },
    legend: { show: false },
    grid: { borderColor: 'var(--mui-palette-divider)', strokeDashArray: 4 }
  }), [])

  // KPI calculations
  const totalTasks = tasks.length
  const completedTasks = taskStatusDistribution.Done
  const overallTaskProgress = totalTasks ? Math.round((completedTasks / totalTasks) * 100) : 0
  const totalEst = tasks.reduce((s, t) => s + Number(t.estimatedHours || 0), 0)
  const totalAct = tasks.reduce((s, t) => s + Number(t.actualHours || 0), 0)
  const totalStories = stories.length
  const totalPts = stories.reduce((s, st) => s + Number(st.storyPoints || 0), 0)

  // Progress bar for effort budget
  const effortPct = totalEst ? Math.min(100, Math.round((totalAct / totalEst) * 100)) : 0
  const effortColor = effortPct > 90 ? '#EA5455' : effortPct > 70 ? '#FF9F43' : '#28C76F'

  return (
    <div className='flex flex-col gap-6'>

      {/* ── KPI Hero Cards ──────────────────────────────── */}
      <Grid container spacing={4}>

        {/* Delivery Progress */}
        <Grid size={{ xs: 12, sm: 6, md: 3 }}>
          <Card
            className='relative overflow-hidden border-0 shadow-md'
            sx={{ background: 'linear-gradient(135deg,#667eea 0%,#764ba2 100%)' }}
          >
            <CardContent className='p-5 flex flex-col gap-1'>
              <Typography variant='caption' sx={{ color: 'rgba(255,255,255,.7)', textTransform: 'uppercase', letterSpacing: '0.1em', fontWeight: 600, fontSize: '0.68rem' }}>
                Delivery Progress
              </Typography>
              <div className='flex items-end justify-between'>
                <Typography variant='h3' sx={{ color: '#fff', fontWeight: 800, lineHeight: 1 }}>
                  {overallTaskProgress}%
                </Typography>
                <i className='tabler-progress-check text-4xl' style={{ color: 'rgba(255,255,255,.4)' }} />
              </div>
              <Typography variant='caption' sx={{ color: 'rgba(255,255,255,.7)' }}>
                {completedTasks} of {totalTasks} tasks done
              </Typography>
              <LinearProgress
                variant='determinate'
                value={overallTaskProgress}
                sx={{ mt: 1.5, height: 6, borderRadius: 3, backgroundColor: 'rgba(255,255,255,.25)', '& .MuiLinearProgress-bar': { backgroundColor: '#fff', borderRadius: 3 } }}
              />
            </CardContent>
          </Card>
        </Grid>

        {/* Story Points */}
        <Grid size={{ xs: 12, sm: 6, md: 3 }}>
          <Card
            className='relative overflow-hidden border-0 shadow-md'
            sx={{ background: 'linear-gradient(135deg,#a18cd1 0%,#fbc2eb 100%)' }}
          >
            <CardContent className='p-5 flex flex-col gap-1'>
              <Typography variant='caption' sx={{ color: 'rgba(255,255,255,.8)', textTransform: 'uppercase', letterSpacing: '0.1em', fontWeight: 600, fontSize: '0.68rem' }}>
                Story Points
              </Typography>
              <div className='flex items-end justify-between'>
                <Typography variant='h3' sx={{ color: '#fff', fontWeight: 800, lineHeight: 1 }}>
                  {totalPts}
                </Typography>
                <i className='tabler-bulb text-4xl' style={{ color: 'rgba(255,255,255,.4)' }} />
              </div>
              <Typography variant='caption' sx={{ color: 'rgba(255,255,255,.8)' }}>
                Across {totalStories} user stories
              </Typography>
              <Typography variant='caption' sx={{ color: 'rgba(255,255,255,.65)', mt: 0.5 }}>
                {totalPts > 0 ? `Avg ${(totalPts / Math.max(totalStories, 1)).toFixed(1)} pts/story` : 'No points set'}
              </Typography>
            </CardContent>
          </Card>
        </Grid>

        {/* Effort Logged */}
        <Grid size={{ xs: 12, sm: 6, md: 3 }}>
          <Card
            className='relative overflow-hidden border-0 shadow-md'
            sx={{ background: 'linear-gradient(135deg,#0ba360 0%,#3cba92 100%)' }}
          >
            <CardContent className='p-5 flex flex-col gap-1'>
              <Typography variant='caption' sx={{ color: 'rgba(255,255,255,.8)', textTransform: 'uppercase', letterSpacing: '0.1em', fontWeight: 600, fontSize: '0.68rem' }}>
                Effort Logged
              </Typography>
              <div className='flex items-end justify-between'>
                <Typography variant='h3' sx={{ color: '#fff', fontWeight: 800, lineHeight: 1 }}>
                  {totalAct}h
                </Typography>
                <i className='tabler-clock-hour-4 text-4xl' style={{ color: 'rgba(255,255,255,.4)' }} />
              </div>
              <Typography variant='caption' sx={{ color: 'rgba(255,255,255,.8)' }}>
                of {totalEst}h estimated budget
              </Typography>
              <LinearProgress
                variant='determinate'
                value={effortPct}
                sx={{ mt: 1.5, height: 6, borderRadius: 3, backgroundColor: 'rgba(255,255,255,.25)', '& .MuiLinearProgress-bar': { backgroundColor: '#fff', borderRadius: 3 } }}
              />
            </CardContent>
          </Card>
        </Grid>

        {/* Defect Rate */}
        <Grid size={{ xs: 12, sm: 6, md: 3 }}>
          <Card
            className='relative overflow-hidden border-0 shadow-md'
            sx={{ background: issues.length > 0 ? 'linear-gradient(135deg,#f5576c 0%,#f093fb 100%)' : 'linear-gradient(135deg,#43e97b 0%,#38f9d7 100%)' }}
          >
            <CardContent className='p-5 flex flex-col gap-1'>
              <Typography variant='caption' sx={{ color: 'rgba(255,255,255,.8)', textTransform: 'uppercase', letterSpacing: '0.1em', fontWeight: 600, fontSize: '0.68rem' }}>
                Active Defects
              </Typography>
              <div className='flex items-end justify-between'>
                <Typography variant='h3' sx={{ color: '#fff', fontWeight: 800, lineHeight: 1 }}>
                  {issues.length}
                </Typography>
                <i className={`${issues.length > 0 ? 'tabler-bug' : 'tabler-shield-check'} text-4xl`} style={{ color: 'rgba(255,255,255,.4)' }} />
              </div>
              <Typography variant='caption' sx={{ color: 'rgba(255,255,255,.8)' }}>
                {severityCounts.Critical > 0 ? `${severityCounts.Critical} critical blocker${severityCounts.Critical > 1 ? 's' : ''}` : issues.length === 0 ? 'No open defects!' : `${severityCounts.High} high priority`}
              </Typography>
            </CardContent>
          </Card>
        </Grid>
      </Grid>

      {/* ── Excel Export Action Banner ─────────────────────── */}
      <Card className='border border-slate-200 bg-white shadow-xs'>
        <CardContent className='p-4 flex flex-wrap items-center justify-between gap-3'>
          <div className='flex items-center gap-3'>
            <div className='w-10 h-10 rounded-xl bg-emerald-50 text-emerald-700 flex items-center justify-center border border-emerald-200'>
              <i className='tabler-file-spreadsheet text-2xl' />
            </div>
            <div>
              <Typography variant='subtitle1' className='font-bold text-slate-900'>
                Comprehensive Project Excel Audit &amp; Performance Report
              </Typography>
              <Typography variant='caption' className='text-slate-500'>
                Multi-sheet workbook containing executive KPIs, team logged effort &amp; contribution, sprint velocity, all user stories, tasks, and quality defect logs.
              </Typography>
            </div>
          </div>
          <Button
            variant='contained'
            color='success'
            className='font-bold shadow-sm bg-emerald-600 hover:bg-emerald-700 text-white'
            startIcon={<i className='tabler-download' />}
            onClick={handleExport}
          >
            Download
          </Button>
        </CardContent>
      </Card>

      {/* ── Row 1: Status Donut + Workload Bar ──────────── */}
      <Grid container spacing={4}>
        <Grid size={{ xs: 12, lg: 5 }}>
          <Card className='border border-gray-200 dark:border-gray-700 shadow-sm h-full'>
            <CardContent className='flex flex-col gap-3 p-5'>
              <div>
                <Typography variant='h6' className='font-bold'>
                  Task Workflow Distribution
                </Typography>
                <Typography variant='body2' color='text.secondary'>
                  Real-time breakdown across all workflow stages
                </Typography>
              </div>
              <div className='flex gap-2 flex-wrap'>
                {[{ label: 'To Do', val: taskStatusDistribution.ToDo, color: '#94a3b8' }, { label: 'In Progress', val: taskStatusDistribution.InProgress, color: '#3b82f6' }, { label: 'In Review', val: taskStatusDistribution.Review, color: '#f59e0b' }, { label: 'Done', val: taskStatusDistribution.Done, color: '#22c55e' }].map(item => (
                  <div key={item.label} className='flex items-center gap-1.5'>
                    <span style={{ width: 10, height: 10, borderRadius: '50%', background: item.color, display: 'inline-block' }} />
                    <Typography variant='caption' color='text.secondary'>{item.label}: <strong>{item.val}</strong></Typography>
                  </div>
                ))}
              </div>
              {totalTasks === 0 ? (
                <div className='h-[240px] flex flex-col items-center justify-center text-gray-400 gap-2'>
                  <i className='tabler-clipboard-list text-3xl' />
                  <span className='text-sm'>No tasks yet. Add tasks to see the distribution.</span>
                </div>
              ) : (
                <Chart options={donutOptions} series={donutSeries} type='donut' height={240} />
              )}
            </CardContent>
          </Card>
        </Grid>

        <Grid size={{ xs: 12, lg: 7 }}>
          <Card className='border border-gray-200 dark:border-gray-700 shadow-sm h-full'>
            <CardContent className='flex flex-col gap-3 p-5'>
              <div>
                <Typography variant='h6' className='font-bold'>
                  Team Workload & Effort
                </Typography>
                <Typography variant='body2' color='text.secondary'>
                  Estimated hours budgeted vs. actual time logged per member
                </Typography>
              </div>
              <Chart options={workloadOptions} series={workloadSeries} type='bar' height={260} />
            </CardContent>
          </Card>
        </Grid>
      </Grid>

      {/* ── Row 2: Sprint Velocity + Severity Breakdown ─── */}
      <Grid container spacing={4}>
        <Grid size={{ xs: 12, lg: 7 }}>
          <Card className='border border-gray-200 dark:border-gray-700 shadow-sm h-full'>
            <CardContent className='flex flex-col gap-3 p-5'>
              <div className='flex items-start justify-between'>
                <div>
                  <Typography variant='h6' className='font-bold'>
                    Sprint Velocity & Burn-Up
                  </Typography>
                  <Typography variant='body2' color='text.secondary'>
                    Committed story points vs. points delivered per sprint
                  </Typography>
                </div>
                <Chip size='small' label={`${sprints.length} Sprint${sprints.length !== 1 ? 's' : ''}`} color='primary' variant='tonal' />
              </div>
              <Chart options={velocityOptions} series={velocitySeries} type='line' height={260} />
            </CardContent>
          </Card>
        </Grid>

        <Grid size={{ xs: 12, lg: 5 }}>
          <Card className='border border-gray-200 dark:border-gray-700 shadow-sm h-full'>
            <CardContent className='flex flex-col gap-3 p-5'>
              <div className='flex items-start justify-between'>
                <div>
                  <Typography variant='h6' className='font-bold'>
                    Defect Severity Breakdown
                  </Typography>
                  <Typography variant='body2' color='text.secondary'>
                    Bugs grouped by urgency level
                  </Typography>
                </div>
                <Chip size='small' label={`${issues.length} bug${issues.length !== 1 ? 's' : ''}`} color={issues.length > 0 ? 'error' : 'success'} />
              </div>
              {issues.length === 0 ? (
                <div className='h-[260px] flex flex-col items-center justify-center gap-3'>
                  <div className='w-16 h-16 rounded-full bg-green-100 dark:bg-green-900/30 flex items-center justify-center'>
                    <i className='tabler-shield-check text-3xl text-green-500' />
                  </div>
                  <div className='text-center'>
                    <Typography variant='subtitle2' className='font-semibold text-green-600'>All Clear!</Typography>
                    <Typography variant='caption' color='text.secondary'>No defects reported. Project quality is healthy.</Typography>
                  </div>
                </div>
              ) : (
                <Chart options={severityOptions} series={severitySeries} type='bar' height={260} />
              )}
            </CardContent>
          </Card>
        </Grid>
      </Grid>
    </div>
  )
}

export default ProjectAnalyticsView
