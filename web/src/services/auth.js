import { endpoints } from '@/api/endpoints'
import { httpPost } from '@/api/httpClient'

export const authService = {
  login: ({ userNameOrEmail, password }) => httpPost(endpoints.auth.login, { userNameOrEmail, password }),
  revoke: refreshToken => httpPost(endpoints.auth.revoke, { refreshToken })
}
