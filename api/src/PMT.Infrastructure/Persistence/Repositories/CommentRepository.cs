using System.Data;
using Dapper;
using PMT.Application.Comments;
using PMT.Application.Common.Interfaces;
using PMT.Domain.Entities;
using PMT.Domain.Enums;

namespace PMT.Infrastructure.Persistence.Repositories;

public sealed class CommentRepository(IDbConnectionFactory f) : ICommentRepository
{
    static string P => ProcedureNames.Get(StoredProcedure.Comment);
    static string A(ProcedureAction a) => ProcedureNames.Action(a);

    public async Task<IReadOnlyCollection<Comment>> GetForEntityAsync(string type, long entityId, CancellationToken ct = default)
    {
        await using var c = f.CreateConnection();
        return (await c.QueryAsync<Comment>(new(P, new { Action = A(ProcedureAction.Fetch), EntityType = type, EntityId = entityId }, commandType: CommandType.StoredProcedure, cancellationToken: ct))).AsList();
    }

    public async Task<Comment?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        await using var c = f.CreateConnection();
        return await c.QuerySingleOrDefaultAsync<Comment>(new(P, new { Action = "FETCH_BY_ID", Id = id }, commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }

    public async Task<long> CreateAsync(Comment e, CancellationToken ct = default)
    {
        await using var c = f.CreateConnection();
        return await c.ExecuteScalarAsync<long>(new(P, new { Action = A(ProcedureAction.Insert), e.ProjectId, e.UserStoryId, e.TaskId, e.IssueId, e.UserId, Content = e.Body, User = e.InsertedBy }, commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }

    public async Task<bool> UpdateAsync(Comment e, CancellationToken ct = default)
    {
        await using var c = f.CreateConnection();
        return await c.ExecuteScalarAsync<long>(new(P, new { Action = A(ProcedureAction.Update), e.Id, Content = e.Body, UserId = e.UserId, User = e.UpdatedBy }, commandType: CommandType.StoredProcedure, cancellationToken: ct)) > 0;
    }

    public async Task<bool> DeleteAsync(long id, long? user, CancellationToken ct = default)
    {
        await using var c = f.CreateConnection();
        return await c.ExecuteScalarAsync<long>(new(P, new { Action = A(ProcedureAction.Delete), Id = id, User = user }, commandType: CommandType.StoredProcedure, cancellationToken: ct)) > 0;
    }
}
