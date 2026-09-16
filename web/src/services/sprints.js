import { endpoints } from '@/api/endpoints'
import { httpDelete, httpGet, httpPost, httpPut } from '@/api/httpClient'
import { optionalText, requiredText, toDateOnly } from '@/libs/payload'

// Sprints live under a SCRUM project and are keyed by Projects.Key, mirroring the board and
// access endpoints. `${sprintsPath(key)}/${sprintId}` is the item route; the lifecycle
// transitions (start/complete) hang off it as action sub-routes.
const sprintsPath = projectKey => endpoints.projectSprints(projectKey)

// UpsertSprintRequest(Name, Goal, StartDate, EndDate). Status is owned by the server and moved
// through the start/complete actions rather than sent from the form. Dates are DateOnly on the
// DTO, so they travel as 'YYYY-MM-DD' like ProjectDto.StartDate/TargetDate.
const toRequest = body => ({
  name: requiredText(body.name),
  goal: optionalText(body.goal),
  startDate: toDateOnly(body.startDate),
  endDate: toDateOnly(body.endDate)
})

export const sprintsService = {
  // A 403 degrades the Sprints tab to empty rather than routing to /forbidden.
  listSprints: (projectKey, params = {}) => httpGet(sprintsPath(projectKey), { params, __suppressForbidden: true }),
  createSprint: (projectKey, data) => httpPost(sprintsPath(projectKey), toRequest(data)),
  updateSprint: (projectKey, sprintId, data) => httpPut(`${sprintsPath(projectKey)}/${sprintId}`, toRequest(data)),
  removeSprint: (projectKey, sprintId) => httpDelete(`${sprintsPath(projectKey)}/${sprintId}`),

  // Lifecycle transitions. The body is empty; the server owns Sprint.Status and moves it in a
  // single atomic transition (start flips Planned -> Active, complete flips Active -> Completed).
  // No actual start/end dates are stamped — the sprint only carries the planned StartDate/EndDate
  // captured on the form.
  startSprint: (projectKey, sprintId) => httpPost(`${sprintsPath(projectKey)}/${sprintId}/start`, {}),
  completeSprint: (projectKey, sprintId) => httpPost(`${sprintsPath(projectKey)}/${sprintId}/complete`, {})
}
