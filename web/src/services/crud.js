import { httpDelete, httpGet, httpPost, httpPut } from '@/api/httpClient'

// The API caps pageSize at 200 (Math.Clamp in every *Service.GetPagedAsync).
export const MAX_PAGE_SIZE = 200

const toListParams = ({ page = 1, pageSize = 25, search } = {}) => {
  const params = { page, pageSize: Math.min(pageSize, MAX_PAGE_SIZE) }

  if (search) params.search = search

  return params
}

/**
 * Builds a service for one of the paged CRUD modules.
 * `toRequest` maps form state onto the module's Upsert request shape; the API rejects
 * stray '' values on nullable numeric fields, so every module supplies one.
 */
export const createCrudService = ({ path, toRequest = body => body }) => ({
  list: params => httpGet(path, { params: toListParams(params) }),
  get: id => httpGet(`${path}/${id}`),
  create: body => httpPost(path, toRequest(body)),
  update: (id, body) => httpPut(`${path}/${id}`, toRequest(body)),
  remove: id => httpDelete(`${path}/${id}`)
})
