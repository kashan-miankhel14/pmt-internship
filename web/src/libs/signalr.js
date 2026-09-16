import * as signalR from '@microsoft/signalr'

// Hook Imports
import { getAccessToken } from '@/libs/session'

/**
 * The API maps the hub at exactly "/hubs/notifications" (Program.cs), which is what
 * NEXT_PUBLIC_HUB_URL points at — the URL is used as-is, with no hub name appended.
 * The token goes over the query string via accessTokenFactory, which the backend reads.
 */
export const createNotificationHub = () => {
  const hubUrl = process.env.NEXT_PUBLIC_HUB_URL

  if (!hubUrl) {
    // eslint-disable-next-line no-console
    console.warn('[signalr] NEXT_PUBLIC_HUB_URL is not configured. Realtime notifications are disabled.')

    return null
  }

  return new signalR.HubConnectionBuilder()
    .withUrl(hubUrl.replace(/\/+$/, ''), {
      accessTokenFactory: () => getAccessToken() ?? ''
    })
    // The default policy stops retrying after ~30s (four attempts). Realtime notifications are a
    // long-lived enhancement, so keep retrying indefinitely with a capped exponential backoff:
    // 1s, 2s, 4s ... up to 30s between attempts. Returning a number (never null) means it never
    // gives up on its own; the SignalRProvider's cleanup is what actually stops the connection.
    .withAutomaticReconnect({
      nextRetryDelayInMilliseconds: retryContext =>
        Math.min(1000 * 2 ** retryContext.previousRetryCount, 30000)
    })
    .configureLogging(signalR.LogLevel.Warning)
    .build()
}
