import { create } from 'axios'

import { attachInterceptors } from '@/api/interceptors'

const apiConfig = {
  baseURL: process.env.NEXT_PUBLIC_API_URL,
  headers: { 'Content-Type': 'application/json' }
}

// Main instance used by every service call.
export const api = create(apiConfig)

// Bare instance used only for the token-refresh call, so a refresh failure
// never recurses into another refresh attempt.
const authApi = create(apiConfig)

attachInterceptors(api, authApi)
