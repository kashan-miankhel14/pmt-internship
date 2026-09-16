import { endpoints } from '@/api/endpoints'
import { httpGet } from '@/api/httpClient'
import { optionalId, optionalText, requiredText, toBool, toDateOnly } from '@/libs/payload'
import { createCrudService } from '@/services/crud'
import { attachmentsService } from '@/services/attachments'

// UpsertProjectRequest(Key, Name, Description, OwnerUserId, Status, StartDate, TargetDate, Active, DepartmentId)
const toRequest = body => ({
  key: requiredText(body.key),
  name: requiredText(body.name),
  description: optionalText(body.description),
  ownerUserId: optionalId(body.ownerUserId),
  status: body.status || 'Planning',
  startDate: toDateOnly(body.startDate),
  targetDate: toDateOnly(body.targetDate),
  active: toBool(body.active),
  departmentId: optionalId(body.departmentId)
})

const crud = createCrudService({ path: endpoints.projects, toRequest })

// Silently returns null on error (403, 404, etc.) so a single failed sub-request does not
// crash the whole project detail aggregate.
const safeGet = (path, params) =>
  httpGet(path, { params, __suppressForbidden: true }).catch(() => null)

const safeAttachments = async projectId =>
  attachmentsService
    .list({ entityType: 'Project', entityId: projectId })
    .then(result => (Array.isArray(result) ? result : result?.items ?? []))
    .catch(() => [])

/**
 * Fetches a full project detail aggregate:
 * - project record
 * - stories, tasks, issues (large page, then filtered client-side to this project)
 * - attachments
 *
 * The API exposes no single aggregate endpoint so this fans out to several calls in parallel.
 * We fetch pageSize:200 of each module and filter client-side because the per-module list
 * endpoints do not accept a projectId filter parameter.
 */
const detail = async id => {
  const projectId = Number(id)
  const pagedParams = { page: 1, pageSize: 200 }

  const [project, stories, tasks, issues, attachments] = await Promise.all([
    crud.get(projectId),
    safeGet(endpoints.userStories, pagedParams),
    safeGet(endpoints.tasks, pagedParams),
    safeGet(endpoints.issues, pagedParams),
    safeAttachments(projectId)
  ])

  // Normalises paged envelope vs bare array, then filters to the requested project.
  const forProject = paged => {
    const items = Array.isArray(paged) ? paged : paged?.items ?? []
    return items.filter(
      item => Number(item.projectId ?? item.ProjectId) === projectId
    )
  }

  return {
    project,
    stories: forProject(stories),
    tasks: forProject(tasks),
    issues: forProject(issues),
    attachments,
    gitLinks: []
  }
}

export const projectsService = { ...crud, detail }
