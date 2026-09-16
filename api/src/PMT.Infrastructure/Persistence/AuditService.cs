using System.Data;using System.Text.Json;using Dapper;using PMT.Application.Common.Interfaces;using PMT.Domain.Enums;
namespace PMT.Infrastructure.Persistence;
public sealed class AuditService(IDbConnectionFactory f,ICurrentUserService current):IAuditService
{
    public async Task WriteAsync(string action, string entityType, long? entityId, object? oldValues = null, object? newValues = null, CancellationToken ct = default)
    {
        // Normalize the incoming action (usually an HTTP verb) to a value the DB CHECK constraint accepts.
        // Map common HTTP methods to ProcedureAction enum values; if the incoming action already matches a
        // ProcedureAction name, prefer that. Default to Insert when unknown.
        var incoming = action?.Trim() ?? string.Empty;
        var incomingUpper = incoming.ToUpperInvariant();

        ProcedureAction procAction = incomingUpper switch
        {
            "GET" => ProcedureAction.Fetch,
            "POST" => ProcedureAction.Insert,
            "PUT" => ProcedureAction.Update,
            "PATCH" => ProcedureAction.Update,
            "DELETE" => ProcedureAction.Delete,
            _ => ProcedureAction.Insert
        };

        // If caller provided a domain action name that matches the enum, prefer that mapping.
        if (!string.IsNullOrWhiteSpace(incoming) && Enum.TryParse<ProcedureAction>(incoming, true, out var parsed))
            procAction = parsed;

        // A read is not an audited mutation; SP_AUDIT_LOG treats FETCH as a query action.
        if (procAction == ProcedureAction.Fetch)
            return;

        // AuditLog.UserId is NOT NULL and carries FK_auditlog_user, so a row can only be written for a
        // known user. Passing 0 used to make the insert fail the foreign key instead of recording anything.
        if (current.UserId is not { } userId || userId <= 0)
            return;

        var parameters = new
        {
            Action = ProcedureNames.Action(procAction),
            EntityName = string.IsNullOrWhiteSpace(entityType) ? "Unknown" : entityType.Trim(),
            EntityId = entityId ?? 0,
            // SP_AUDIT_LOG persists EventAction in AuditLog.Action, which CK_auditlog_action limits to
            // INSERT/UPDATE/DELETE. Pass the normalized procedure action so the value always satisfies it.
            EventAction = ProcedureNames.Action(procAction),
            OldValue = oldValues is null ? null : JsonSerializer.Serialize(oldValues),
            NewValue = newValues is null ? null : JsonSerializer.Serialize(newValues),
            UserId = userId
        };

        await using var c = f.CreateConnection();
        await c.ExecuteAsync(new CommandDefinition(
            ProcedureNames.Get(StoredProcedure.AuditLog),
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: ct));
    }
}
