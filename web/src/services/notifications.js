import { endpoints } from '@/api/endpoints'
import { httpGet, httpPost } from '@/api/httpClient'

/**
 * The whole controller is gated by the `notifications.manage` policy, so a viewer that
 * only holds `*.view` permissions gets a 403 from this *read*. The global 403 handler
 * routes to /forbidden, which used to eject the user out of any page the bell renders on
 * (every dashboard page), so the read opts out of that redirect and degrades to an empty
 * inbox instead.
 */
export const notificationsService = {
  listMine: ({ unreadOnly = false } = {}) =>
    httpGet(endpoints.notifications.mine, { params: { unreadOnly }, __suppressForbidden: true }),
  markRead: id => httpPost(endpoints.notifications.read(id))
}
