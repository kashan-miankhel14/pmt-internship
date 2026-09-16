namespace PMT.Api.Extensions;

/// <summary>
/// Centralized request-body and upload size limits.
/// </summary>
public static class PmtLimits
{
    /// <summary>Default maximum request body size enforced for all endpoints (10 MB).</summary>
    public const long MaxRequestBodySizeBytes = 10 * 1024 * 1024;

    /// <summary>Maximum request body size allowed for a single attachment upload (20 MB).</summary>
    public const long MaxUploadSizeBytes = 20 * 1024 * 1024;
}
