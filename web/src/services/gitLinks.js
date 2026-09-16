import { endpoints } from '@/api/endpoints'
import { httpDelete, httpGet, httpPost } from '@/api/httpClient'
import { optionalText, requiredText } from '@/libs/payload'

// CreateGitLinkRequest(EntityType, EntityId, Provider, RepositoryUrl, ReferenceType, ReferenceId, ReferenceUrl)
export const gitLinksService = {
  // GitLinksController is gated by `git.manage`, so this read 403s for view-only roles.
  // Suppress the global 403 → /forbidden redirect: the panel degrades to an empty list.
  list: ({ entityType, entityId }) =>
    httpGet(endpoints.gitLinks, {
      params: { entityType, entityId: Number(entityId) },
      __suppressForbidden: true
    }),
  create: body =>
    httpPost(endpoints.gitLinks, {
      entityType: body.entityType,
      entityId: Number(body.entityId),
      provider: body.provider,
      repositoryUrl: requiredText(body.repositoryUrl),
      referenceType: requiredText(body.referenceType),
      referenceId: requiredText(body.referenceId),
      referenceUrl: optionalText(body.referenceUrl)
    }),
  remove: id => httpDelete(`${endpoints.gitLinks}/${id}`)
}
