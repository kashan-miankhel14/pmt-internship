namespace PMT.Domain.Exceptions;

/// <summary>
/// Raised when the caller is authenticated but is not allowed to touch the
/// requested resource (for example another user's chat session). Mapped to HTTP 403.
/// </summary>
public sealed class ForbiddenAccessException(string message = "You do not have access to this resource.")
    : Exception(message);
