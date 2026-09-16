import { endpoints } from '@/api/endpoints'
import { httpDelete, httpGet, httpPost, httpPut } from '@/api/httpClient'

/**
 * The API only persists comments against a task or an issue: CommentService maps
 * EntityType onto TaskId/IssueId, and CK_comment_parentrequired rejects a row where
 * both are null. Any other entity type reads back empty and fails to insert.
 */
export const COMMENTABLE_ENTITY_TYPES = ['Project', 'UserStory', 'Task', 'Issue']

export const isCommentable = entityType => COMMENTABLE_ENTITY_TYPES.includes(entityType)

// CommentDto[] — not paged. CreateCommentRequest(EntityType, EntityId, Body) -> { id }.
export const commentsService = {
  // CommentsController is gated by `comments.manage`, so this read 403s for view-only
  // roles. Suppress the global 403 → /forbidden redirect and degrade to an empty thread.
  list: ({ entityType, entityId }) =>
    httpGet(endpoints.comments, {
      params: { entityType, entityId: Number(entityId) },
      __suppressForbidden: true
    }),
  create: ({ entityType, entityId, body }) =>
    httpPost(endpoints.comments, { entityType, entityId: Number(entityId), body }),
  update: ({ id, body }) => httpPut(`${endpoints.comments}/${id}`, { id, body }),
  remove: id => httpDelete(`${endpoints.comments}/${id}`)
}
