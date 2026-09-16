'use client'

// MUI Imports
import MenuItem from '@mui/material/MenuItem'

// Component Imports
import CustomTextField from '@core/components/mui/TextField'

// Hook Imports
import { useProjectRoles } from '@/hooks/useProjectAccess'

/**
 * Project role dropdown (Project Admin > Member > Viewer). Roles come from `/project-roles`
 * and fall back to the seeded list when that endpoint is unavailable, so a grant dialog is
 * never left without a selectable role.
 *
 * `value`/`onChange` speak the role id, which is what ProjectMembers/ProjectTeams store.
 */
const RoleSelect = ({
  value,
  onChange,
  label = 'Project role',
  helperText,
  error,
  disabled,
  required,
  size = 'small',
  fullWidth = true,
  className,

  // Optional override for callers that already hold the role list (avoids a second read).
  options
}) => {
  const { roles, isLoading } = useProjectRoles()
  const items = options ?? roles

  return (
    <CustomTextField
      select
      size={size}
      fullWidth={fullWidth}
      className={className}
      label={label}
      required={required}
      error={error}
      helperText={helperText}
      disabled={disabled || (isLoading && !items.length)}
      value={items.some(role => role.id === value) ? value : ''}
      onChange={event => onChange?.(Number(event.target.value))}
    >
      {items.map(role => (
        <MenuItem key={role.id} value={role.id}>
          {role.name}
        </MenuItem>
      ))}
    </CustomTextField>
  )
}

export default RoleSelect
