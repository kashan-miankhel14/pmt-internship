import { endpoints } from '@/api/endpoints'
import { httpDelete, httpGet, httpPost, httpPut } from '@/api/httpClient'
import { optionalNumber, requiredText } from '@/libs/payload'

// BoardColumns are seeded by the project template (SCRUM/BASIC: To Do, In Progress, Done;
// KANBAN: Backlog, Selected, In Progress, Review, Done). They are keyed by Projects.Key like
// the access endpoints, so every call takes the project key rather than the numeric id.
const columnsPath = projectKey => endpoints.projectColumns(projectKey)

// UpsertBoardColumnRequest(Name, Ordinal, CompleteColumn). Ordinal drives left-to-right order;
// CompleteColumn flags the "done" column. Only Name is strictly required by the wizard-seeded
// board, so the other two travel as sensible defaults when omitted.
const toRequest = body => ({
  name: requiredText(body.name),
  ordinal: optionalNumber(body.ordinal),
  completeColumn: Boolean(body.completeColumn)
})

export const boardService = {
  // A 403 degrades the board to its template defaults (see useBoardColumns) rather than
  // routing the whole page to /forbidden.
  listColumns: (projectKey, params = {}) => httpGet(columnsPath(projectKey), { params, __suppressForbidden: true }),
  addColumn: (projectKey, data) => httpPost(columnsPath(projectKey), toRequest(data)),
  updateColumn: (projectKey, columnId, data) => httpPut(`${columnsPath(projectKey)}/${columnId}`, toRequest(data)),
  removeColumn: (projectKey, columnId) => httpDelete(`${columnsPath(projectKey)}/${columnId}`)
}
