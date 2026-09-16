'use client'

// React Imports
import { useMemo, useState } from 'react'

// MUI Imports
import Avatar from '@mui/material/Avatar'
import Chip from '@mui/material/Chip'
import Typography from '@mui/material/Typography'

// Third-party Imports
import { keepPreviousData, useQuery } from '@tanstack/react-query'

// Component Imports
import CustomAutocomplete from '@core/components/mui/Autocomplete'
import CustomTextField from '@core/components/mui/TextField'

// Hook Imports
import { useDebounce } from '@/hooks/useDebounce'

// Service Imports
import { usersService } from '@/services/users'

export const userLabel = user => user?.displayName || user?.userName || user?.email || ''

const initials = user =>
  userLabel(user)
    .split(' ')
    .filter(Boolean)
    .slice(0, 2)
    .map(part => part[0])
    .join('')
    .toUpperCase()

/**
 * User lookup backed by the paged `/users` endpoint. Search runs server-side and is debounced,
 * so the picker never holds the whole directory in memory.
 *
 * `value` is a user object (or an array of them when `multiple`), not an id: a selection has to
 * stay renderable after the search text changes and drops it out of the current result page.
 */
const UserPicker = ({
  value,
  onChange,
  label = 'User',
  multiple = false,
  placeholder = 'Search by name, username or email',
  helperText,
  error,
  disabled,
  required,
  size = 'small',

  // Ids already spoken for elsewhere on the screen (existing members, the current lead, ...).
  excludeIds = []
}) => {
  const [inputValue, setInputValue] = useState('')
  const search = useDebounce(inputValue, 400)

  const { data, isFetching } = useQuery({
    queryKey: ['user-picker', search],
    queryFn: () => usersService.search({ search, pageSize: 25 }),

    // Keeps the previous page mounted while the next search resolves.
    placeholderData: keepPreviousData,
    staleTime: 60000
  })

  const options = useMemo(() => {
    const selected = multiple ? (value ?? []) : value ? [value] : []
    const excluded = new Set(excludeIds.map(Number))
    const merged = (data?.items ?? []).filter(user => !excluded.has(Number(user.id)))

    // MUI warns when a selected value is missing from the option list, which happens as soon as
    // the search text stops matching it, so selections are merged back in.
    selected.forEach(user => {
      if (user && !merged.some(item => item.id === user.id)) merged.unshift(user)
    })

    return merged
  }, [data?.items, excludeIds, multiple, value])

  return (
    <CustomAutocomplete
      multiple={multiple}
      disabled={disabled}
      size={size}
      fullWidth
      options={options}
      value={multiple ? (value ?? []) : (value ?? null)}
      onChange={(_, next) => onChange?.(next)}
      inputValue={inputValue}
      onInputChange={(_, next, reason) => {
        // 'reset' fires on blur/selection; honouring it would clear the box and refetch page 1.
        if (reason !== 'reset') setInputValue(next)
      }}
      getOptionLabel={option => userLabel(option)}
      isOptionEqualToValue={(option, selected) => option.id === selected.id}

      // Filtering already happened server-side; the default client filter would hide results.
      filterOptions={items => items}
      loading={isFetching}
      loadingText='Searching users…'
      noOptionsText={search ? 'No users match that search' : 'Start typing to search users'}
      renderOption={(props, option) => {
        const { key, ...optionProps } = props

        return (
          <li key={option.id ?? key} {...optionProps}>
            <div className='flex items-center gap-3'>
              <Avatar className='is-8 bs-8 bg-primaryLight text-primary text-xs'>{initials(option)}</Avatar>
              <div className='flex flex-col'>
                <Typography variant='body2' className='font-medium'>
                  {userLabel(option)}
                </Typography>
                <Typography variant='caption' color='text.secondary'>
                  {option.email ?? option.userName}
                </Typography>
              </div>
            </div>
          </li>
        )
      }}
      renderTags={(selected, getTagProps) =>
        selected.map((option, index) => {
          const { key, ...tagProps } = getTagProps({ index })

          return (
            <Chip
              key={option.id ?? key}
              size='small'
              variant='tonal'
              color='primary'
              label={userLabel(option)}
              {...tagProps}
            />
          )
        })
      }
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

export default UserPicker
