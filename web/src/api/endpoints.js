/**
 * Centralised REST endpoint paths. Services import from here instead of hardcoding
 * URL strings, so a path change (or an API version bump) only touches one file.
 *
 * Endpoints that embed an id use a builder function (e.g. `roles`) so the literal
 * `{id}` placeholder is never sent to the server.
 */
export const endpoints = {
  auth: {
    login: '/auth/login',
    refresh: '/auth/refresh',
    revoke: '/auth/revoke',
    revokeAll: '/auth/revoke-all'
  },

  departments: '/departments',

  users: {
    base: '/users',
    availableRoles: '/users/available-roles',
    roles: userId => `/users/${userId}/roles`,
    resetPassword: userId => `/users/${userId}/reset-password`,
    changePassword: userId => `/users/${userId}/change-password`
  },

  projects: '/projects',

  /*
    Project-scoped board and sprint collections. These are keyed by Projects.Key (not id) the
    same way the access endpoints in projectAccess.js are, so they are builder functions:
      projectSprints('PMT') -> '/projects/PMT/sprints'
      projectColumns('PMT') -> '/projects/PMT/columns'
    Item routes hang off these bases (e.g. `${projectSprints(key)}/${sprintId}/complete`).
  */
  projectSprints: key => `/projects/${key}/sprints`,
  projectColumns: key => `/projects/${key}/columns`,

  /*
    Jira-flow (stages 2-4) collections. NEXT_PUBLIC_API_URL already carries the `/api/v1`
    prefix, so these stay relative like every other entry here: writing '/api/v1/teams'
    would resolve to '/api/v1/api/v1/teams'.
  */
  teams: '/teams',
  projectRoles: '/project-roles',

  userStories: '/user-stories',
  tasks: '/tasks',
  issues: '/issues',
  attachments: '/attachments',
  gitLinks: '/git-links',
  comments: '/comments',

  notifications: {
    mine: '/notifications/mine',
    read: id => `/notifications/${id}/read`
  },

  reports: {
    velocity: '/reports/velocity',
    workload: '/reports/workload'
  },

  // AiAgentController is routed absolutely as api/v1/ai/agent, which is exactly
  // NEXT_PUBLIC_API_URL + these paths.
  ai: {
    chat: '/ai/agent/chat',

    // Approves or cancels the destructive tool calls the agent parked behind a
    // confirmation token (`ChatResponseDto.requiresConfirmation`).
    confirm: '/ai/agent/confirm',
    sessions: '/ai/agent/sessions',
    session: id => `/ai/agent/sessions/${id}`,
    messages: id => `/ai/agent/sessions/${id}/messages`,
    reindex: '/ai/agent/admin/reindex',
    reindexStatus: '/ai/agent/admin/reindex/status'
  }
}
