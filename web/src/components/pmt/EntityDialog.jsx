'use client'

import { useEffect, useMemo, useRef } from 'react'

import { Controller, useForm, useWatch } from 'react-hook-form'

import Dialog from '@mui/material/Dialog'
import DialogTitle from '@mui/material/DialogTitle'
import DialogContent from '@mui/material/DialogContent'
import DialogActions from '@mui/material/DialogActions'
import Grid from '@mui/material/Grid'
import MenuItem from '@mui/material/MenuItem'
import Button from '@mui/material/Button'
import FormControlLabel from '@mui/material/FormControlLabel'
import Switch from '@mui/material/Switch'
import Fade from '@mui/material/Fade'

import CustomTextField from '@core/components/mui/TextField'

import { useSprints } from '@/hooks/useSprints'

const defaultValueByType = type => {
  if (type === 'switch') return true

  return ''
}

const buildDefaults = fields =>
  fields.reduce((accumulator, field) => {
    accumulator[field.name] = field.defaultValue ?? defaultValueByType(field.type)

    return accumulator
  }, {})

/**
 * Form values the dialog is reset with. A record straight off the API prefills every field by
 * name, because the DTO properties are camelCase and match the field names one for one
 * (`assignedToUserId`, `sprintId`, ...) — so an edit opens on the values it was saved with.
 *
 * Sprint pickers are normalised on top of that spread: they hold a numeric sprint id, and
 * `UserStoryDto.SprintId` arrives as `sprintId` (null when the story sits outside a sprint).
 * Coercing blanks to null keeps the "No sprint" option selected instead of leaving the select
 * on an empty string it has no item for.
 */
const buildInitialValues = (fields, record) => {
  const values = { ...buildDefaults(fields), ...(record ?? {}) }

  fields.forEach(field => {
    if (field.type !== 'sprint') return

    const value = record?.[field.name]

    values[field.name] = value === '' || value == null ? null : Number(value)
  })

  return values
}

// The project select emits the numeric id from its MenuItem while a record carries whatever the
// API serialised, so both sides are normalised before a difference counts as a project change.
const normalizeProjectId = value => (value === '' || value == null ? '' : Number(value))

/**
 * Project-scoped sprint picker. Only the stories module declares a `type: 'sprint'` field
 * (moduleMeta.stories.sprintId -> UpsertUserStoryRequest.SprintId), so no other entity gets it.
 *
 * Sprints cannot travel in the global `lookups` payload the other selects read from: the API
 * exposes them per project and keys them by Projects.Key (`/projects/{key}/sprints`), while the
 * form only holds the numeric `projectId`. The key is therefore resolved out of the projects
 * lookup and handed to useSprints, which stays disabled until a project is chosen.
 *
 * The selection is cleared whenever the user switches the form to a different project — a
 * sprint from project A is meaningless on project B. Reset-driven project changes (the dialog
 * being opened on an existing story) must be excluded, or an edit would lose its saved sprint:
 * this field mounts before the parent effect that calls `reset(record)` runs, so on the first
 * render of an edit the watched `projectId` is still the previous (empty) form value. The
 * baseline is therefore seeded from the incoming record instead of from that stale value, and
 * re-seeded whenever the dialog is handed a different record.
 */
const SprintSelectField = ({ field, control, setValue, lookups, record, projectFieldName = 'projectId' }) => {
  const projectId = useWatch({ control, name: projectFieldName })

  const projectKey = useMemo(() => {
    if (!projectId) return null

    return (lookups?.projects ?? []).find(project => project.id === Number(projectId))?.key ?? null
  }, [projectId, lookups?.projects])

  const { sprints, isLoading } = useSprints(projectKey)

  const recordRef = useRef(record)
  const previousProjectRef = useRef(normalizeProjectId(record?.[projectFieldName]))
  const hasSyncedRef = useRef(false)

  useEffect(() => {
    // First pass: nothing to compare. The baseline already holds the incoming record's project
    // while the watched value is still whatever the form carried before `reset(record)` ran, so
    // treating the difference as a user edit here would wipe the sprint the record was saved with.
    if (!hasSyncedRef.current) {
      hasSyncedRef.current = true

      return
    }

    if (recordRef.current !== record) {
      recordRef.current = record
      previousProjectRef.current = normalizeProjectId(record?.[projectFieldName])

      return
    }

    const nextProjectId = normalizeProjectId(projectId)

    if (previousProjectRef.current !== nextProjectId) {
      previousProjectRef.current = nextProjectId
      setValue(field.name, null)
    }
  }, [record, projectId, projectFieldName, field.name, setValue])

  const helperText = !projectKey
    ? 'Select a project first — sprints are defined per project.'
    : !isLoading && !sprints.length
      ? 'This project has no sprints yet.'
      : undefined

  return (
    <Controller
      name={field.name}
      control={control}
      render={({ field: controllerField }) => {
        // MUI warns when a select holds a value none of its items carry, which happens on the
        // first render of an edit (the sprint list is still loading). The stored value is kept
        // in form state either way; only the rendered value falls back to the empty option.
        const selected = sprints.some(sprint => sprint.id === Number(controllerField.value))
          ? Number(controllerField.value)
          : ''

        return (
          <CustomTextField
            {...controllerField}
            select
            fullWidth
            disabled={!projectKey}
            label={field.label}
            value={selected}
            onChange={event => controllerField.onChange(event.target.value === '' ? null : Number(event.target.value))}
            helperText={helperText}
          >
            <MenuItem value=''>No sprint</MenuItem>
            {sprints.map(sprint => (
              <MenuItem key={sprint.id} value={sprint.id}>
                {sprint.status ? `${sprint.name} · ${sprint.status}` : sprint.name}
              </MenuItem>
            ))}
          </CustomTextField>
        )
      }}
    />
  )
}

const EntityDialog = ({
  open,
  title,
  fields,
  record,
  lookups,
  onClose,
  onSubmit,
  onDelete,
  submitLabel = 'Save changes',
  loading,
  extraContent
}) => {
  const { control, handleSubmit, reset, setValue } = useForm({ defaultValues: buildDefaults(fields) })

  useEffect(() => {
    reset(buildInitialValues(fields, record))
  }, [fields, record, reset])

  const formatFieldValue = (field, value) => {
    if (value == null) return ''

    if (field.type === 'datetime-local' && typeof value === 'string') {
      return value.slice(0, 16)
    }

    return value
  }

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth='md'>
      <DialogTitle>{title}</DialogTitle>
      <DialogContent>
        <Fade in timeout={300}>
          <div>
            <Grid container spacing={4} className='pt-1'>
          {fields.map(field => {
            if (field.type === 'switch') {
              return (
                <Grid key={field.name} size={{ xs: 12 }}>
                  <Controller
                    name={field.name}
                    control={control}
                    render={({ field: controllerField }) => (
                      <FormControlLabel
                        control={
                          <Switch
                            checked={Boolean(controllerField.value)}
                            onChange={event => controllerField.onChange(event.target.checked)}
                          />
                        }
                        label={field.label}
                      />
                    )}
                  />
                </Grid>
              )
            }

            // Sprints are project-scoped, so they need their own query rather than a slice of
            // the shared lookups payload. Declared by moduleMeta.stories only.
            if (field.type === 'sprint') {
              return (
                <Grid key={field.name} size={{ xs: 12, md: 6 }}>
                  <SprintSelectField
                    field={field}
                    control={control}
                    setValue={setValue}
                    lookups={lookups}
                    record={record}
                  />
                </Grid>
              )
            }

            const options = field.options ?? lookups?.[field.optionsKey] ?? []

            return (
              <Grid key={field.name} size={{ xs: 12, md: field.type === 'multiline' ? 12 : 6 }}>
                <Controller
                  name={field.name}
                  control={control}
                  rules={{ required: field.required ? `${field.label} is required.` : false }}
                  render={({ field: controllerField, fieldState }) => (
                    <CustomTextField
                      {...controllerField}
                      fullWidth
                      select={field.type === 'select'}
                      type={
                        field.type === 'number' ||
                        field.type === 'password' ||
                        field.type === 'email' ||
                        field.type === 'date' ||
                        field.type === 'datetime-local'
                          ? field.type
                          : 'text'
                      }
                      label={field.label}
                      placeholder={field.placeholder ?? field.label}
                      multiline={field.type === 'multiline'}
                      minRows={field.type === 'multiline' ? 4 : undefined}
                      value={formatFieldValue(field, controllerField.value)}
                      onChange={event =>
                        controllerField.onChange(
                          field.type === 'number'
                            ? event.target.value === ''
                              ? null
                              : Number(event.target.value)
                            : field.type === 'select' && event.target.value === ''
                              ? null
                              : event.target.value
                        )
                      }
                      error={Boolean(fieldState.error)}
                      helperText={fieldState.error?.message}
                    >
                      {field.type === 'select' ? (
                        [
                          !field.required ? (
                            <MenuItem key='empty-none' value=''>
                              <em className='text-textDisabled'>None / Unassigned</em>
                            </MenuItem>
                          ) : null,
                          ...options.map(option => (
                            <MenuItem key={option.id ?? option} value={option.id ?? option}>
                              {option.name ?? option.title ?? option.displayName ?? option.label ?? option}
                            </MenuItem>
                          ))
                        ]
                      ) : null}
                    </CustomTextField>
                  )}
                />
              </Grid>
            )
          })}
        </Grid>
        {/* Slot for record-scoped panels (e.g. the comment thread of a saved task/issue). */}
        {extraContent ? <div className='mbs-6'>{extraContent}</div> : null}
          </div>
        </Fade>
      </DialogContent>
      <DialogActions>
        {record && onDelete ? (
          <Button color='error' onClick={onDelete} disabled={loading} className='me-auto'>
            Delete
          </Button>
        ) : null}
        <Button onClick={onClose}>Cancel</Button>
        <Button variant='contained' onClick={handleSubmit(onSubmit)} disabled={loading}>
          {submitLabel}
        </Button>
      </DialogActions>
    </Dialog>
  )
}

export default EntityDialog
