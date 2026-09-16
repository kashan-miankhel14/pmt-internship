import { endpoints } from '@/api/endpoints'
import { optionalId, optionalText, requiredId, requiredText, toBool } from '@/libs/payload'
import { createCrudService } from '@/services/crud'

// UpsertIssueRequest(ProjectId, TaskId, Title, Description, Severity, Status, ReportedByUserId, AssignedToUserId, Active)
const toRequest = body => ({
  projectId: requiredId(body.projectId),
  taskId: optionalId(body.taskId),
  title: requiredText(body.title),
  description: optionalText(body.description),
  severity: body.severity || 'Medium',
  status: body.status || 'Open',
  reportedByUserId: optionalId(body.reportedByUserId),
  assignedToUserId: optionalId(body.assignedToUserId),
  teamId: optionalId(body.teamId),
  active: toBool(body.active)
})

export const issuesService = createCrudService({ path: endpoints.issues, toRequest })
