import { endpoints } from '@/api/endpoints'
import { optionalId, optionalNumber, optionalText, requiredId, requiredText, toBool, toDateTime, toPriority } from '@/libs/payload'
import { createCrudService } from '@/services/crud'

// UpsertTaskRequest(ProjectId, UserStoryId, Title, Description, Status, Priority, AssignedToUserId, EstimatedHours, ActualHours, DueDate, Active)
const toRequest = body => ({
  projectId: requiredId(body.projectId),
  userStoryId: optionalId(body.userStoryId),
  title: requiredText(body.title),
  description: optionalText(body.description),
  status: body.status || 'ToDo',
  priority: toPriority(body.priority),
  assignedToUserId: optionalId(body.assignedToUserId),
  teamId: optionalId(body.teamId),
  estimatedHours: optionalNumber(body.estimatedHours),
  actualHours: optionalNumber(body.actualHours),
  dueDate: toDateTime(body.dueDate),
  active: toBool(body.active)
})

export const tasksService = createCrudService({ path: endpoints.tasks, toRequest })
