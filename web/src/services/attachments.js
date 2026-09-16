import { endpoints } from '@/api/endpoints'
import { httpDelete, httpDownload, httpGet, httpUpload } from '@/api/httpClient'

export const attachmentsService = {
  list: ({ entityType, entityId }) => httpGet(endpoints.attachments, { params: { entityType, entityId: Number(entityId) }, __suppressForbidden: true }),
  upload: ({ entityType, entityId, file, onUploadProgress }) =>
    httpUpload(endpoints.attachments, file, {
      fields: { entityType, entityId: Number(entityId) },
      onUploadProgress
    }),
  download: id => httpDownload(`${endpoints.attachments}/${id}/download`),
  remove: id => httpDelete(`${endpoints.attachments}/${id}`)
}
