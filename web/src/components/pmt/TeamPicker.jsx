'use client'

// React Imports
import { useMemo, useState } from 'react'

// MUI Imports
import Typography from '@mui/material/Typography'

// Third-party Imports
import { keepPreviousData, useQuery } from '@tanstack/react-query'

// Component Imports
import CustomAutocomplete from '@core/components/mui/Autocomplete'
import CustomTextField from '@core/components/mui/TextField'

// Hook Imports
import { useDebounce } from '@/hooks/useDebounce'

// Service Imports
import { teamsService } from '@/services/teams'

/**
 * Team lookup used when granting a team access to a project (stage 4, team path).
 * Mirrors UserPicker: server-side debounced search, `value` is a team object.
 */
const TeamPicker = ({
  value,
  onChange,
  label = 'Team',
  placeholder = 'Search teams by name or key',
  helperText,
  error,
  disabled,
  required,
  size = 'small',

  // Teams that already hold a grant on this project.
  excludeIds = []
}) => {
  const [inputValue, setInputValue] = useState('')
  const search = useDebounce(inputValue, 400)

  const { data, isFetching } = useQuery({
    queryKey: ['team-picker', search],
    queryFn: () => teamsService.search({ page: 1, pageSize: 25, search }),
    placeholderData: keepPreviousData,
    staleTime: 60000,

    // Teams are a stage-2 module; a missing endpoint must not spam retries behind a dialog.
    retry: false
  })

  const options = useMemo(() => {
    const excluded = new Set(excludeIds.map(Number))
    const merged = (data?.items ?? []).filter(team => !excluded.has(Number(team.id)))

    if (value && !merged.some(item => item.id === value.id)) merged.unshift(value)

    return merged
  }, [data?.items, excludeIds, value])

  return (
    <CustomAutocomplete
      disabled={disabled}
      size={size}
      fullWidth
      options={options}
      value={value ?? null}
      onChange={(_, next) => onChange?.(next)}
      inputValue={inputValue}
      onInputChange={(_, next, reason) => {
        if (reason !== 'reset') setInputValue(next)
      }}
      getOptionLabel={option => option?.name ?? ''}
      isOptionEqualToValue={(option, selected) => option.id === selected.id}
      filterOptions={items => items}
      loading={isFetching}
      loadingText='Searching teams…'
      noOptionsText={search ? 'No teams match that search' : 'Start typing to search teams'}
      renderOption={(props, option) => {
        const { key, ...optionProps } = props

        return (
          <li key={option.id ?? key} {...optionProps}>
            <div className='flex flex-col'>
              <Typography variant='body2' className='font-medium'>
                {option.name}
              </Typography>
              <Typography variant='caption' color='text.secondary'>
                {option.key}
                {option.memberCount != null ? ` · ${option.memberCount} members` : ''}
              </Typography>
            </div>
          </li>
        )
      }}
      renderInput={params => (
        <CustomTextField
          {...params}
          label={label}
          required={required}
          placeholder={placeholder}
          error={error}
          helperText={helperText}
        />
      )}
    />
  )
}

export default TeamPicker
