'use client'

import ModuleTable from '@/components/pmt/ModuleTable'

const ModuleView = ({ moduleKey, ...restProps }) => (
  <ModuleTable moduleKey={moduleKey} {...restProps} />
)

export default ModuleView
