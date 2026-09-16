namespace PMT.Application.Common.Interfaces;

public interface IAuditService
{
    Task WriteAsync(string action, string entityType, long? entityId, object? oldValues = null, object? newValues = null, CancellationToken cancellationToken = default);
}
