using System.Data;using Dapper;using PMT.Application.Common.Interfaces;using PMT.Domain.Entities;using PMT.Domain.Enums;using PMT.Infrastructure.BackgroundJobs;
namespace PMT.Infrastructure.Persistence.Repositories;
internal sealed class JobQueueRepository(IDbConnectionFactory f):IJobQueueRepository
{
 static string P=>ProcedureNames.Get(StoredProcedure.JobQueue);static string A(ProcedureAction a)=>ProcedureNames.Action(a);
 public async Task<long>EnqueueAsync(JobQueueItem i,CancellationToken ct=default){await using var c=f.CreateConnection();return await c.ExecuteScalarAsync<long>(new(P,new{Action=A(ProcedureAction.Insert),i.JobType,i.Payload,Status=i.Status.ToString(),i.Attempts,User=i.InsertedBy},commandType:CommandType.StoredProcedure,cancellationToken:ct));}
 public async Task<JobQueueItem?>ClaimNextAsync(CancellationToken ct=default){await using var c=f.CreateConnection();return await c.QuerySingleOrDefaultAsync<JobQueueItem>(new(P,new{Action=A(ProcedureAction.Claim)},commandType:CommandType.StoredProcedure,cancellationToken:ct));}
 public async Task CompleteAsync(long id,CancellationToken ct=default){await using var c=f.CreateConnection();await c.ExecuteAsync(new CommandDefinition(P,new{Action=A(ProcedureAction.Complete),Id=id},commandType:CommandType.StoredProcedure,cancellationToken:ct));}
 public async Task FailAsync(long id,string error,DateTime retryAt,CancellationToken ct=default){await using var c=f.CreateConnection();await c.ExecuteAsync(new CommandDefinition(P,new{Action=A(ProcedureAction.Fail),Id=id,LastError=error,AvailableDate=retryAt},commandType:CommandType.StoredProcedure,cancellationToken:ct));}
}
