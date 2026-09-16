using System.Data;using Dapper;using PMT.Application.Common.Interfaces;using PMT.Application.GitLinks;using PMT.Domain.Entities;using PMT.Domain.Enums;
namespace PMT.Infrastructure.Persistence.Repositories;
public sealed class GitLinkRepository(IDbConnectionFactory f):IGitLinkRepository
{
 static string P=>ProcedureNames.Get(StoredProcedure.GitLink);static string A(ProcedureAction a)=>ProcedureNames.Action(a);
 public async Task<IReadOnlyCollection<GitLink>>GetForEntityAsync(string type,long entityId,CancellationToken ct=default){await using var c=f.CreateConnection();return(await c.QueryAsync<GitLink>(new(P,new{Action=A(ProcedureAction.Fetch),EntityType=type,EntityId=entityId},commandType:CommandType.StoredProcedure,cancellationToken:ct))).AsList();}
  public async Task<long>CreateAsync(GitLink e,CancellationToken ct=default){await using var c=f.CreateConnection();return await c.ExecuteScalarAsync<long>(new(P,new{Action=A(ProcedureAction.Insert),e.ProjectId,e.TaskId,e.IssueId,Provider=e.Provider.ToString(),e.CommitSha,e.PullRequestUrl,e.RepositoryUrl,e.ReferenceType,User=e.InsertedBy},commandType:CommandType.StoredProcedure,cancellationToken:ct));}
 public async Task<bool>DeleteAsync(long id,long? user,CancellationToken ct=default){await using var c=f.CreateConnection();return await c.ExecuteScalarAsync<long>(new(P,new{Action=A(ProcedureAction.Delete),Id=id,User=user},commandType:CommandType.StoredProcedure,cancellationToken:ct))>0;}
}
