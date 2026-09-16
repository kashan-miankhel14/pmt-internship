import { api } from '@/api/axiosInstance'

/**
 * Generic HTTP verbs used by every services/*.js module. Centralising them here
 * means the axios instance, response unwrapping, and multipart handling only
 * need to be correct in one place.
 */
const unwrap = response => response.data

export const httpGet = (url, config) => api.get(url, config).then(unwrap)
export const httpPost = (url, body, config) => api.post(url, body, config).then(unwrap)
export const httpPut = (url, body, config) => api.put(url, body, config).then(unwrap)
export const httpPatch = (url, body, config) => api.patch(url, body, config).then(unwrap)
export const httpDelete = (url, config) => api.delete(url, config).then(unwrap)
export const httpDownload = (url, config) => api.get(url, { ...config, responseType: 'blob' }).then(response => response)

/**
 * Multipart file upload. `file` is a File/Blob; `fields` are extra form fields
 * (e.g. entityType/entityId) sent alongside it.
 */
export const httpUpload = (url, file, { fieldName = 'file', fields = {}, onUploadProgress, ...config } = {}) => {
  const formData = new FormData()

  formData.append(fieldName, file)
  Object.entries(fields).forEach(([key, value]) => formData.append(key, value))

  return api
    .post(url, formData, {
      ...config,
      headers: { 'Content-Type': 'multipart/form-data', ...(config.headers || {}) },
      onUploadProgress
    })
    .then(unwrap)
}
