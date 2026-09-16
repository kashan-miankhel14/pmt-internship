import { issueSeverities, issueStatuses, priorities, projectStatuses, storyStatuses, taskStatuses } from '@/libs/enums'

export const moduleMeta = {
  departments: {
    title: 'Departments',
    singular: 'department',
    route: '/departments',
    columns: ['code', 'name', 'description', 'active'],
    formFields: [
      { name: 'code', label: 'Code', type: 'text', required: true },
      { name: 'name', label: 'Name', type: 'text', required: true },
      { name: 'description', label: 'Description', type: 'multiline' },
      { name: 'active', label: 'Active', type: 'switch' }
    ]
  },
  users: {
    title: 'Users',
    singular: 'user',
    route: '/users',
    columns: ['displayName', 'userName', 'email', 'departmentId', 'roleId', 'active', 'isLocked'],
    formFields: [
      { name: 'displayName', label: 'Display name', type: 'text', required: true },
      { name: 'userName', label: 'Username', type: 'text', required: true },
      { name: 'email', label: 'Email', type: 'email', required: true },
      { name: 'departmentId', label: 'Department', type: 'select', optionsKey: 'departments' },
      { name: 'roleId', label: 'Role', type: 'select', optionsKey: 'roles' },

      // UpsertUserValidator only enforces a minimum length when a password is supplied,
      // so leaving this blank on edit keeps the existing password.
      {
        name: 'password',
        label: 'Password',
        type: 'password',
        helperText: 'Minimum 10 characters. Leave blank to keep the current password.'
      },
      { name: 'active', label: 'Active', type: 'switch' }
    ]
  },
  projects: {
    title: 'Projects',
    singular: 'project',
    route: '/projects',
    columns: ['key', 'name', 'status', 'ownerUserId', 'startDate', 'targetDate', 'active'],
    formFields: [
      {
        name: 'key',
        label: 'Key',
        type: 'text',
        required: true,
        helperText: 'Letters, digits, _ and -, starting with a letter.'
      },
      { name: 'name', label: 'Name', type: 'text', required: true },
      { name: 'description', label: 'Description', type: 'multiline' },

      // ProjectService.Apply throws a validation error when either of these is null.
      { name: 'ownerUserId', label: 'Owner', type: 'select', optionsKey: 'users', required: true },
      { name: 'departmentId', label: 'Department', type: 'select', optionsKey: 'departments', required: true },
      { name: 'status', label: 'Status', type: 'select', options: projectStatuses, defaultValue: 'Planning' },
      { name: 'startDate', label: 'Start date', type: 'date' },
      { name: 'targetDate', label: 'Target date', type: 'date' },
      { name: 'active', label: 'Active', type: 'switch' }
    ]
  },
  stories: {
    title: 'User Stories',
    singular: 'story',
    route: '/stories',
    columns: ['title', 'projectId', 'status', 'priority', 'storyPoints', 'assignedToUserId', 'active'],
    formFields: [
      { name: 'projectId', label: 'Project', type: 'select', optionsKey: 'projects', required: true },
      { name: 'title', label: 'Title', type: 'text', required: true },
      { name: 'description', label: 'Description', type: 'multiline' },
      { name: 'acceptanceCriteria', label: 'Acceptance criteria', type: 'multiline' },
      { name: 'status', label: 'Status', type: 'select', options: storyStatuses, defaultValue: 'Backlog' },
      { name: 'priority', label: 'Priority', type: 'select', options: priorities, defaultValue: 3 },
      { name: 'storyPoints', label: 'Story points', type: 'number' },
      { name: 'assignedToUserId', label: 'Assignee', type: 'select', optionsKey: 'users' },
      { name: 'teamId', label: 'Team', type: 'select', optionsKey: 'teams' },

      // Sprints are per-project (keyed by Projects.Key), so they cannot ride along in the global
      // lookups payload the other selects read. `type: 'sprint'` tells EntityDialog to resolve
      // the project key from the chosen projectId and load that project's sprints; the value
      // maps onto UpsertUserStoryRequest.SprintId (see services/stories.js).
      { name: 'sprintId', label: 'Sprint', type: 'sprint' },
      { name: 'active', label: 'Active', type: 'switch' }
    ]
  },
  tasks: {
    title: 'Tasks',
    singular: 'task',
    route: '/tasks',
    columns: ['title', 'projectId', 'status', 'priority', 'assignedToUserId', 'estimatedHours', 'dueDate', 'active'],
    formFields: [
      { name: 'projectId', label: 'Project', type: 'select', optionsKey: 'projects', required: true },

      // Task.userStoryId is optional in the API (optionalId in the service payload mapper).
      { name: 'userStoryId', label: 'Story', type: 'select', optionsKey: 'stories' },
      { name: 'title', label: 'Title', type: 'text', required: true },
      { name: 'description', label: 'Description', type: 'multiline' },
      { name: 'status', label: 'Status', type: 'select', options: taskStatuses, defaultValue: 'ToDo' },
      { name: 'priority', label: 'Priority', type: 'select', options: priorities, defaultValue: 3 },
      { name: 'assignedToUserId', label: 'Assignee', type: 'select', optionsKey: 'users' },
      { name: 'teamId', label: 'Team', type: 'select', optionsKey: 'teams' },
      { name: 'estimatedHours', label: 'Estimated hours', type: 'number' },
      { name: 'actualHours', label: 'Actual hours', type: 'number' },
      { name: 'dueDate', label: 'Due date', type: 'datetime-local' },
      { name: 'active', label: 'Active', type: 'switch' }
    ]
  },
  issues: {
    title: 'Issues',
    singular: 'issue',
    route: '/issues',
    columns: ['title', 'projectId', 'taskId', 'severity', 'status', 'assignedToUserId', 'active'],
    formFields: [
      { name: 'projectId', label: 'Project', type: 'select', optionsKey: 'projects', required: true },
      { name: 'taskId', label: 'Task', type: 'select', optionsKey: 'tasks' },
      { name: 'title', label: 'Title', type: 'text', required: true },
      { name: 'description', label: 'Description', type: 'multiline' },
      { name: 'severity', label: 'Severity', type: 'select', options: issueSeverities, defaultValue: 'Medium' },
      { name: 'status', label: 'Status', type: 'select', options: issueStatuses, defaultValue: 'Open' },
      { name: 'reportedByUserId', label: 'Reported by', type: 'select', optionsKey: 'users' },
      { name: 'assignedToUserId', label: 'Assigned to', type: 'select', optionsKey: 'users' },
      { name: 'teamId', label: 'Team', type: 'select', optionsKey: 'teams' },
      { name: 'active', label: 'Active', type: 'switch' }
    ]
  }
}
