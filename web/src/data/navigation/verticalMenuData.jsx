const verticalMenuData = () => [
  {
    isSection: true,
    label: 'Workspace'
  },
  {
    label: 'Dashboard',
    href: '/home',
    icon: 'tabler-smart-home',
    action: 'view',
    subject: 'reports'
  },
  {
    label: 'Projects',
    href: '/projects',
    icon: 'tabler-briefcase',
    action: 'view',
    subject: 'projects'
  },
  {
    isSection: true,
    label: 'Organization'
  },
  {
    /*
      Teams are granted project access as a unit, and the API permission catalogue has no
      `teams.*` claim yet, so this entry rides on the projects grant: everyone who can see
      projects can see the teams that reach them.
    */
    label: 'Teams',
    href: '/teams',
    icon: 'tabler-users-group',
    action: 'view',
    subject: 'projects'
  },
  {
    label: 'Users',
    href: '/users',
    icon: 'tabler-users',
    action: 'view',
    subject: 'users'
  },
  {
    label: 'Departments',
    href: '/departments',
    icon: 'tabler-building-community',
    action: 'view',
    subject: 'departments'
  },
  {
    isSection: true,
    label: 'Global Catalogs'
  },
  {
    label: 'All User Stories',
    href: '/stories',
    icon: 'tabler-bulb',
    action: 'view',
    subject: 'stories'
  },
  {
    label: 'All Tasks',
    href: '/tasks',
    icon: 'tabler-checklist',
    action: 'view',
    subject: 'tasks'
  },
  {
    label: 'All Issues & Bugs',
    href: '/issues',
    icon: 'tabler-bug',
    action: 'view',
    subject: 'issues'
  }
]

export default verticalMenuData
