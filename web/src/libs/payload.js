import { DEFAULT_PRIORITY } from '@/libs/enums'

const isBlank = value => value === '' || value === null || value === undefined

// Select inputs yield '' when cleared. The API binds those fields to long?/decimal?,
// which rejects '' with a 400, so blanks have to travel as null.
export const optionalId = value => (isBlank(value) ? null : Number(value))

export const optionalNumber = value => {
  if (isBlank(value)) return null

  const parsed = Number(value)

  return Number.isNaN(parsed) ? null : parsed
}

export const optionalText = value => {
  if (isBlank(value)) return null

  const trimmed = String(value).trim()

  return trimmed === '' ? null : trimmed
}

export const requiredId = value => Number(value) || 0
export const requiredText = value => String(value ?? '').trim()
export const toBool = value => value !== false
export const toPriority = value => Number(value) || DEFAULT_PRIORITY

// ProjectDto uses DateOnly, which only accepts 'YYYY-MM-DD'.
export const toDateOnly = value => {
  if (isBlank(value)) return null

  const date = new Date(value)

  return Number.isNaN(date.getTime()) ? null : date.toISOString().slice(0, 10)
}

// TaskDto.DueDate is a DateTime; the datetime-local input gives a local, zone-less string.
export const toDateTime = value => {
  if (isBlank(value)) return null

  const date = new Date(value)

  return Number.isNaN(date.getTime()) ? null : date.toISOString()
}

// datetime-local only renders 'YYYY-MM-DDTHH:mm', never an ISO string with a zone.
export const toDateTimeLocalInput = value => {
  if (isBlank(value)) return ''

  const date = new Date(value)

  if (Number.isNaN(date.getTime())) return ''

  const offset = date.getTimezoneOffset() * 60000

  return new Date(date.getTime() - offset).toISOString().slice(0, 16)
}

export const toDateInput = value => (isBlank(value) ? '' : String(value).slice(0, 10))
