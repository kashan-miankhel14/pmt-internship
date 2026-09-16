// These lists must mirror the API enums exactly (PMT.Domain.Enums.*): the payload is
// deserialised straight onto the enum, so any extra member is rejected with a 400.
export const projectStatuses = ['Planning', 'Active', 'OnHold', 'Completed', 'Cancelled']
export const storyStatuses = ['Backlog', 'Ready', 'InProgress', 'Review', 'Done', 'Cancelled']
export const taskStatuses = ['ToDo', 'InProgress', 'Blocked', 'Review', 'Done', 'Cancelled']
export const issueStatuses = ['Open', 'InProgress', 'Resolved', 'Closed', 'Rejected']
export const issueSeverities = ['Low', 'Medium', 'High', 'Critical']
export const gitProviders = ['GitHub', 'GitLab', 'AzureDevOps', 'Other']
export const entityTypes = ['Project', 'UserStory', 'Task', 'Issue']

// The API models priority as an int, not a string enum: UpsertTaskValidator and
// UpsertUserStoryValidator both require InclusiveBetween(1, 5), and the entity default is 3.
export const DEFAULT_PRIORITY = 3

export const priorities = [
  { id: 1, name: 'Highest' },
  { id: 2, name: 'High' },
  { id: 3, name: 'Medium' },
  { id: 4, name: 'Low' },
  { id: 5, name: 'Lowest' }
]

export const priorityLabel = value => priorities.find(item => item.id === Number(value))?.name ?? '--'

export const priorityColor = value => {
  const num = Number(value)
  if (num === 1) return 'error'
  if (num === 2) return 'warning'
  if (num === 3) return 'info'
  if (num === 4) return 'primary'
  return 'secondary'
}

