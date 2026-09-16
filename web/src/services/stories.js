import { endpoints } from '@/api/endpoints'
import { optionalId, optionalNumber, optionalText, requiredId, requiredText, toBool, toPriority } from '@/libs/payload'
import { createCrudService } from '@/services/crud'

// UpsertUserStoryRequest(ProjectId, Title, Description, AcceptanceCriteria, Status, Priority, StoryPoints, AssignedToUserId, SprintId, Active)
const toRequest = body => ({
  projectId: requiredId(body.projectId),
  title: requiredText(body.title),
  description: optionalText(body.description),
  acceptanceCriteria: optionalText(body.acceptanceCriteria),
  status: body.status || 'Backlog',
  priority: toPriority(body.priority),
  storyPoints: optionalNumber(body.storyPoints),
  assignedToUserId: optionalId(body.assignedToUserId),
  teamId: optionalId(body.teamId),

  // Sprint assignment. The story dialog now carries a project-scoped sprint picker (moduleMeta
  // declares `sprintId` as `type: 'sprint'` and EntityDialog resolves the project key from the
  // chosen projectId), so this forwards whatever the form holds and null when it is cleared.
  sprintId: optionalId(body.sprintId),
  active: toBool(body.active)
})

export const storiesService = createCrudService({ path: endpoints.userStories, toRequest })
