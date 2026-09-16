const FALLBACK_MESSAGE = 'Something went wrong. Please try again.'

/**
 * Normalises an API error body into a list of display strings.
 *
 * ASP.NET ProblemDetails frequently answers with an empty `errors` array/object (500s,
 * 401s, and any failure produced outside FluentValidation). Returning that empty array
 * verbatim meant callers rendered `undefined` in a toast, so an empty collection now
 * falls back to the ProblemDetails `title`/`detail`.
 */
export const extractErrors = data => {
  if (typeof data === 'string' && data.trim()) return [data.trim()]

  const fallback = [data?.title || data?.detail || FALLBACK_MESSAGE]

  if (Array.isArray(data?.errors)) {
    const messages = data.errors.filter(Boolean)

    return messages.length ? messages : fallback
  }

  if (data?.errors && typeof data.errors === 'object') {
    const messages = Object.values(data.errors).flat().filter(Boolean)

    return messages.length ? messages : fallback
  }

  return fallback
}
