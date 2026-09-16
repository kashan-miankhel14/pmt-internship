using System.Data;
using Dapper;
using PMT.Application.Common.Interfaces;
using PMT.Application.Common.Models;
using PMT.Application.Users;
using PMT.Domain.Entities;
using PMT.Domain.Enums;

namespace PMT.Infrastructure.Persistence.Repositories;

public sealed class UserRepository(IDbConnectionFactory connectionFactory) : IUserRepository
{
    private static string Proc => ProcedureNames.Get(StoredProcedure.User);
    private static string Action(ProcedureAction action) => ProcedureNames.Action(action);

    public async Task<PagedResult<User>> GetPagedAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        using var grid = await connection.QueryMultipleAsync(new CommandDefinition(Proc,
            new { Action=Action(ProcedureAction.Paged), Search=string.IsNullOrWhiteSpace(search)?null:search.Trim(), Page=page, PageSize=pageSize },
            commandType:CommandType.StoredProcedure,cancellationToken:cancellationToken));
        var total=await grid.ReadSingleAsync<long>(); var rows=(await grid.ReadAsync<User>()).AsList();
        return new(rows,page,pageSize,total);
    }

    public async Task<User?> GetByIdAsync(long id,CancellationToken cancellationToken=default)
    { await using var c=connectionFactory.CreateConnection(); return await c.QuerySingleOrDefaultAsync<User>(new(Proc,new {Action=Action(ProcedureAction.Fetch),Id=id},commandType:CommandType.StoredProcedure,cancellationToken:cancellationToken)); }

    public async Task<long> CreateAsync(User e,CancellationToken cancellationToken=default)
    { await using var c=connectionFactory.CreateConnection(); return await c.ExecuteScalarAsync<long>(new(Proc,new {Action=Action(ProcedureAction.Insert),e.FullName,e.Email,e.PasswordHash,e.RoleId,e.DepartmentId,e.Active,UserId=e.InsertedBy},commandType:CommandType.StoredProcedure,cancellationToken:cancellationToken)); }

    public async Task<bool> UpdateAsync(User e,CancellationToken cancellationToken=default)
    { await using var c=connectionFactory.CreateConnection(); return await c.ExecuteScalarAsync<long>(new(Proc,new {Action=Action(ProcedureAction.Update),e.Id,e.FullName,e.Email,e.PasswordHash,e.RoleId,e.DepartmentId,e.Active,UserId=e.UpdatedBy},commandType:CommandType.StoredProcedure,cancellationToken:cancellationToken))>0; }

    public async Task<bool> DeleteAsync(long id,long? deletedBy,CancellationToken cancellationToken=default)
    { await using var c=connectionFactory.CreateConnection(); return await c.ExecuteScalarAsync<long>(new(Proc,new {Action=Action(ProcedureAction.Delete),Id=id,UserId=deletedBy},commandType:CommandType.StoredProcedure,cancellationToken:cancellationToken))>0; }

    public async Task<IReadOnlyCollection<Role>> GetAvailableRolesAsync(CancellationToken cancellationToken=default)
    { await using var c=connectionFactory.CreateConnection(); return (await c.QueryAsync<Role>(new(Proc,new {Action=Action(ProcedureAction.Roles)},commandType:CommandType.StoredProcedure,cancellationToken:cancellationToken))).AsList(); }

    public async Task<IReadOnlyCollection<Role>> GetUserRolesAsync(long userId,CancellationToken cancellationToken=default)
    { await using var c=connectionFactory.CreateConnection(); return (await c.QueryAsync<Role>(new(Proc,new {Action=Action(ProcedureAction.Roles),Id=userId},commandType:CommandType.StoredProcedure,cancellationToken:cancellationToken))).AsList(); }

    public async Task<bool> SetUserRolesAsync(long userId,IReadOnlyCollection<long> roleIds,long? updatedBy,CancellationToken cancellationToken=default)
    {
        // Support assigning multiple roles to a user. Iterate over each roleId and execute the stored procedure.
        await using var c = connectionFactory.CreateConnection();
        foreach (var roleId in roleIds)
        {
            var result = await c.ExecuteScalarAsync<long>(
                new(Proc, new { Action = Action(ProcedureAction.SetRole), Id = userId, RoleId = roleId, UserId = updatedBy },
                commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken));
            if (result == 0)
            {
                // If any role assignment fails, abort and return false.
                return false;
            }
        }
        return true;
    }

    public async Task<bool> UpdatePasswordAsync(long id, string passwordHash, long? updatedBy, CancellationToken cancellationToken = default)
    {
        await using var c = connectionFactory.CreateConnection();
        return await c.ExecuteScalarAsync<long>(new CommandDefinition(
            Proc,
            new { Action = Action(ProcedureAction.UpdatePassword), Id = id, PasswordHash = passwordHash, UserId = updatedBy },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken)) > 0;
    }
}
