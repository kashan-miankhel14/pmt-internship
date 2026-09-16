'use client'

// Next Imports
import Link from 'next/link'

// MUI Imports


// Component Imports
import ModuleView from '@views/modules/ModuleView'

// Context Imports


/**
 * Projects registry with the guided creation flow on top. The registry's own "New project"
 * button opens the quick single-form dialog; this one starts the stage-3 wizard, which also
 * seeds the board, columns and access level.
 */
const ProjectsListView = () => (
  <div className='flex flex-col gap-4'>
    <ModuleView
      moduleKey='projects'
      subject='projects'
      intro='Projects are the central PMT module. This list already supports status chips, ownership, date windows and deep-linking into the detail workspace.'
    />
  </div>
)

export default ProjectsListView
