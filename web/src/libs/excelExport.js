/**
 * PMT Professional Project Excel / Spreadsheet Exporter
 * Generates true OpenXML Excel workbooks (.xlsx) with multi-sheet structure:
 * 1. Executive Summary
 * 2. Team Workload
 * 3. Sprints
 * 4. User Stories
 * 5. Technical Tasks
 * 6. Defects & Issues
 */

import * as XLSX from 'xlsx'
import { priorityLabel } from '@/libs/enums'

const formatDate = val => {
  if (!val) return 'N/A'
  const d = new Date(val)
  if (isNaN(d.getTime())) return 'N/A'
  return d.toISOString().split('T')[0]
}

export const exportProjectToExcel = (projectData, lookups = {}) => {
  if (!projectData || !projectData.project) {
    throw new Error('Project data is required to export.')
  }

  const project = projectData.project
  const stories = projectData.stories || []
  const tasks = projectData.tasks || []
  const issues = projectData.issues || []
  const sprints = projectData.sprints || []

  const usersList = lookups.users || []
  const userMap = {}
  usersList.forEach(u => {
    userMap[u.id] = u.displayName || u.email || u.userName
  })

  const departmentName = lookups.departments?.find(d => d.id === project.departmentId)?.name || 'General'
  const ownerName = userMap[project.ownerUserId] || 'Unassigned'

  // Calculations
  const totalStories = stories.length
  const totalTasks = tasks.length
  const completedTasks = tasks.filter(t => String(t.status).toLowerCase().includes('done')).length
  const inProgressTasks = tasks.filter(t => String(t.status).toLowerCase().includes('progress')).length
  const reviewTasks = tasks.filter(t => String(t.status).toLowerCase().includes('review')).length
  const todoTasks = tasks.filter(t => String(t.status).toLowerCase().includes('todo')).length

  const totalStoryPoints = stories.reduce((sum, s) => sum + Number(s.storyPoints || 0), 0)
  const completedStoryPoints = stories
    .filter(s => String(s.status).toLowerCase().includes('done'))
    .reduce((sum, s) => sum + Number(s.storyPoints || 0), 0)

  const totalEstHours = tasks.reduce((sum, t) => sum + Number(t.estimatedHours || 0), 0)
  const totalActHours = tasks.reduce((sum, t) => sum + Number(t.actualHours || 0), 0)
  const completionRate = totalTasks > 0 ? Math.round((completedTasks / totalTasks) * 100) : 0

  // 1. Executive Summary Data
  const summaryAOA = [
    ['PROJECT EXECUTIVE SUMMARY', ''],
    ['Generated Date', new Date().toISOString().split('T')[0]],
    ['', ''],
    ['Core Project Metadata', ''],
    ['Project Key', (project.key || 'N/A').toUpperCase()],
    ['Project Name', project.name || 'Untitled Project'],
    ['Status', project.status || 'Active'],
    ['Department', departmentName],
    ['Project Owner', ownerName],
    ['Start Date', formatDate(project.startDate)],
    ['Target Date', formatDate(project.targetDate)],
    ['Description', project.description || 'No description provided.'],
    ['', ''],
    ['Key Performance & Scope Metrics', 'Value'],
    ['Overall Completion Rate', `${completionRate}%`],
    ['Total User Stories', totalStories],
    ['Total Story Points', totalStoryPoints],
    ['Completed Story Points', completedStoryPoints],
    ['Total Technical Tasks', totalTasks],
    ['Completed Tasks', completedTasks],
    ['In-Progress Tasks', inProgressTasks],
    ['In-Review Tasks', reviewTasks],
    ['To-Do / Open Tasks', todoTasks],
    ['Total Estimated Hours', `${totalEstHours} hrs`],
    ['Total Actual Hours Logged', `${totalActHours} hrs`],
    ['Total Logged Defects / Issues', issues.length],
    ['Open / In-Progress Defects', issues.filter(i => !['closed', 'resolved', 'done'].includes(String(i.status).toLowerCase())).length]
  ]

  // 2. Team Workload Data
  const teamWorkload = {}
  tasks.forEach(t => {
    const userId = t.assignedToUserId || t.assigneeUserId || 0
    const name = userMap[userId] || 'Unassigned'
    if (!teamWorkload[name]) {
      teamWorkload[name] = {
        name,
        totalTasks: 0,
        doneTasks: 0,
        inProgressTasks: 0,
        todoTasks: 0,
        estHours: 0,
        actHours: 0,
        points: 0
      }
    }
    teamWorkload[name].totalTasks += 1
    const st = String(t.status || '').toLowerCase()
    if (st.includes('done')) teamWorkload[name].doneTasks += 1
    else if (st.includes('progress')) teamWorkload[name].inProgressTasks += 1
    else teamWorkload[name].todoTasks += 1

    teamWorkload[name].estHours += Number(t.estimatedHours || 0)
    teamWorkload[name].actHours += Number(t.actualHours || 0)
  })

  stories.forEach(s => {
    const userId = s.assignedToUserId || s.assigneeUserId || 0
    const name = userMap[userId] || 'Unassigned'
    if (teamWorkload[name]) {
      teamWorkload[name].points += Number(s.storyPoints || 0)
    }
  })

  const teamAOA = [
    ['Team Member', 'Total Tasks', 'Completed Tasks', 'In Progress', 'To Do', 'Story Points', 'Est. Hours', 'Act. Hours', 'Efficiency']
  ]

  Object.values(teamWorkload).forEach(m => {
    const eff = m.estHours > 0 && m.actHours > 0 ? `${Math.round((m.estHours / m.actHours) * 100)}%` : '100%'
    teamAOA.push([
      m.name,
      m.totalTasks,
      m.doneTasks,
      m.inProgressTasks,
      m.todoTasks,
      m.points,
      m.estHours,
      m.actHours,
      eff
    ])
  })

  // 3. Sprints Data
  const sprintAOA = [
    ['Sprint Name', 'Status', 'Start Date', 'End Date', 'Stories Count', 'Story Points', 'Sprint Goal']
  ]

  sprints.forEach(sp => {
    const spStories = stories.filter(s => s.sprintId === sp.id)
    const spPoints = spStories.reduce((sum, s) => sum + Number(s.storyPoints || 0), 0)
    sprintAOA.push([
      sp.name || `Sprint ${sp.id}`,
      sp.status || 'Planning',
      formatDate(sp.startDate),
      formatDate(sp.endDate),
      spStories.length,
      spPoints,
      sp.goal || 'No goal set'
    ])
  })

  // 4. User Stories Data
  const storyAOA = [
    ['Story ID', 'Story Title', 'Status', 'Priority', 'Story Points', 'Sprint', 'Assignee', 'Description']
  ]

  stories.forEach(st => {
    const sprintName = sprints.find(sp => sp.id === st.sprintId)?.name || 'Backlog'
    const assignee = userMap[st.assignedToUserId || st.assigneeUserId] || 'Unassigned'
    storyAOA.push([
      `STORY-${st.id}`,
      st.title || 'Untitled',
      st.status || 'Backlog',
      priorityLabel(st.priority),
      st.storyPoints ?? 0,
      sprintName,
      assignee,
      st.description || ''
    ])
  })

  // 5. Technical Tasks Data
  const taskAOA = [
    ['Task ID', 'Task Title', 'Parent Story', 'Status', 'Priority', 'Assignee', 'Est. Hours', 'Act. Hours', 'Due Date', 'Description']
  ]

  tasks.forEach(tk => {
    const parentStory = stories.find(s => s.id === (tk.userStoryId || tk.storyId))
    const storyRef = parentStory ? `STORY-${parentStory.id}: ${parentStory.title}` : 'Standalone'
    const assignee = userMap[tk.assignedToUserId || tk.assigneeUserId] || 'Unassigned'
    taskAOA.push([
      `TASK-${tk.id}`,
      tk.title || 'Untitled',
      storyRef,
      tk.status || 'ToDo',
      priorityLabel(tk.priority),
      assignee,
      tk.estimatedHours ?? 0,
      tk.actualHours ?? 0,
      formatDate(tk.dueDate),
      tk.description || ''
    ])
  })

  // 6. Defects & Issues Data
  const issueAOA = [
    ['Issue ID', 'Issue Title', 'Severity', 'Status', 'Linked Task', 'Reported By', 'Assignee', 'Reported Date', 'Description']
  ]

  issues.forEach(is => {
    const linkedTask = tasks.find(t => t.id === is.taskId)
    const taskRef = linkedTask ? `TASK-${linkedTask.id}: ${linkedTask.title}` : 'General'
    const reporter = userMap[is.reportedByUserId] || 'System'
    const assignee = userMap[is.assignedToUserId] || 'Unassigned'
    issueAOA.push([
      `ISSUE-${is.id}`,
      is.title || 'Untitled',
      is.severity || 'Medium',
      is.status || 'Open',
      taskRef,
      reporter,
      assignee,
      formatDate(is.createdAt || is.createdOn),
      is.description || ''
    ])
  })

  // Create Workbook
  const workbook = XLSX.utils.book_new()

  const summarySheet = XLSX.utils.aoa_to_sheet(summaryAOA)
  const teamSheet = XLSX.utils.aoa_to_sheet(teamAOA)
  const sprintSheet = XLSX.utils.aoa_to_sheet(sprintAOA)
  const storySheet = XLSX.utils.aoa_to_sheet(storyAOA)
  const taskSheet = XLSX.utils.aoa_to_sheet(taskAOA)
  const issueSheet = XLSX.utils.aoa_to_sheet(issueAOA)

  // Column width formatting
  summarySheet['!cols'] = [{ wch: 30 }, { wch: 45 }]
  teamSheet['!cols'] = [{ wch: 22 }, { wch: 14 }, { wch: 16 }, { wch: 14 }, { wch: 12 }, { wch: 14 }, { wch: 14 }, { wch: 14 }, { wch: 14 }]
  sprintSheet['!cols'] = [{ wch: 25 }, { wch: 16 }, { wch: 14 }, { wch: 14 }, { wch: 14 }, { wch: 14 }, { wch: 35 }]
  storySheet['!cols'] = [{ wch: 14 }, { wch: 35 }, { wch: 16 }, { wch: 14 }, { wch: 14 }, { wch: 22 }, { wch: 22 }, { wch: 45 }]
  taskSheet['!cols'] = [{ wch: 14 }, { wch: 35 }, { wch: 30 }, { wch: 16 }, { wch: 14 }, { wch: 22 }, { wch: 12 }, { wch: 12 }, { wch: 14 }, { wch: 45 }]
  issueSheet['!cols'] = [{ wch: 14 }, { wch: 35 }, { wch: 14 }, { wch: 16 }, { wch: 30 }, { wch: 20 }, { wch: 20 }, { wch: 16 }, { wch: 45 }]

  XLSX.utils.book_append_sheet(workbook, summarySheet, 'Executive Summary')
  XLSX.utils.book_append_sheet(workbook, teamSheet, 'Team Workload')
  XLSX.utils.book_append_sheet(workbook, sprintSheet, 'Sprints')
  XLSX.utils.book_append_sheet(workbook, storySheet, 'User Stories')
  XLSX.utils.book_append_sheet(workbook, taskSheet, 'Technical Tasks')
  XLSX.utils.book_append_sheet(workbook, issueSheet, 'Defects & Issues')

  // Generate and download .xlsx file
  const cleanKey = (project.key || 'PROJECT').toUpperCase()
  const dateStr = new Date().toISOString().split('T')[0]
  const fileName = `${cleanKey}_Project_Report_${dateStr}.xlsx`

  XLSX.writeFile(workbook, fileName, { bookType: 'xlsx', type: 'binary' })
}
