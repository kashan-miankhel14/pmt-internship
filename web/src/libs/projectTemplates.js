/**
 * Stage 2-4 reference data from the Jira flow spec (teams, project templates, access levels,
 * project roles). These mirror CHECK constraints and seeded rows on the API side, so the
 * values must stay byte-identical to what the database accepts.
 */

// core.Projects.TypeCode CHECK ('SCRUM','KANBAN','BASIC')
export const projectTemplates = [
  {
    code: 'SCRUM',
    name: 'Scrum',
    icon: 'tabler-run',
    description: 'Sprint-based delivery with a backlog, sprint planning, burndown and velocity.',
    columns: 'To Do · In Progress · Done'
  },
  {
    code: 'KANBAN',
    name: 'Kanban',
    icon: 'tabler-layout-kanban',
    description: 'Continuous flow with WIP limits and a five-column board, no sprints.',
    columns: 'Backlog · Selected · In Progress · Review · Done'
  },
  {
    code: 'BASIC',
    name: 'Basic',
    icon: 'tabler-checklist',
    description: 'A simple task list for small pieces of work. Three columns, no ceremonies.',
    columns: 'To Do · In Progress · Done'
  }
]

// core.Projects.AccessLevel CHECK ('OPEN','RESTRICTED','PRIVATE')
export const accessLevels = [
  {
    code: 'OPEN',
    name: 'Open',
    icon: 'tabler-world',
    description: 'Every authenticated user can browse and work on this project.'
  },
  {
    code: 'RESTRICTED',
    name: 'Restricted',
    icon: 'tabler-users-group',
    description: 'Only project members and granted teams can see the project.'
  },
  {
    code: 'PRIVATE',
    name: 'Private',
    icon: 'tabler-lock',
    description: 'Invite only. Members are added one by one by a project admin.'
  }
]

// core.TeamMembers.TeamRole CHECK ('Lead','Member','Guest')
export const teamRoles = ['Lead', 'Member', 'Guest']

/*
  Seeded core.ProjectRoles rows, lowest SortOrder = highest privilege. Used as the fallback
  list when `GET /project-roles` is unavailable so a role dropdown is never empty.
*/
export const fallbackProjectRoles = [
  { id: 1, name: 'Project Admin', sortOrder: 1 },
  { id: 2, name: 'Member', sortOrder: 2 },
  { id: 3, name: 'Viewer', sortOrder: 3 }
]

// Keys are uppercase, start with a letter, 2-10 characters. Shared by teams and projects.
export const KEY_PATTERN = /^[A-Z][A-Z0-9]{1,9}$/

export const isValidKey = value => KEY_PATTERN.test(String(value ?? ''))

/**
 * Derives a key from a name the way the spec describes: initials of every word
 * ("UDL Project Management Tool" -> "UPMT"), or the first four letters when the name is a
 * single word ("Apollo" -> "APOL"). Always user-editable afterwards.
 */
export const deriveKey = name => {
  const words = String(name ?? '')
    .toUpperCase()
    .replace(/[^A-Z0-9]+/g, ' ')
    .split(' ')
    .filter(Boolean)

  if (!words.length) return ''

  const initials = words.map(word => word[0]).join('')
  const candidate = initials.length > 1 ? initials : words[0].slice(0, 4)

  // A key must start with a letter, so any leading digits are dropped before trimming to 10.
  return candidate.replace(/^[^A-Z]+/, '').slice(0, 10)
}

export const templateByCode = code => projectTemplates.find(item => item.code === code)
export const accessLevelByCode = code => accessLevels.find(item => item.code === code)
