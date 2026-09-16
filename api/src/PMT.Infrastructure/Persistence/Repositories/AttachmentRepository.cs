using System.Data; using Dapper; using PMT.Application.Attachments; using PMT.Application.Common.Interfaces; using PMT.Domain.Entities; using PMT.Domain.Enums;
namespace PMT.Infrastructure.Persistence.Repositories;
public sealed class AttachmentRepository(IDbConnectionFactory f):IAttachmentRepository
{
 static string P=>ProcedureNames.Get(StoredProcedure.Attachment);static string A(ProcedureAction a)=>ProcedureNames.Action(a);
 public async Task<IReadOnlyCollection<Attachment>>GetForEntityAsync(string type,long entityId,CancellationToken ct=default){await using var c=f.CreateConnection();return(await c.QueryAsync<Attachment>(new(P,new{Action=A(ProcedureAction.Fetch),EntityType=type,EntityId=entityId},commandType:CommandType.StoredProcedure,cancellationToken:ct))).AsList();}
 public async Task<Attachment?>GetByIdAsync(long id,CancellationToken ct=default){await using var c=f.CreateConnection();return await c.QuerySingleOrDefaultAsync<Attachment>(new(P,new{Action="FETCH_BY_ID",Id=id},commandType:CommandType.StoredProcedure,cancellationToken:ct));}
 public async Task<long>CreateAsync(Attachment e,CancellationToken ct=default){await using var c=f.CreateConnection();return await c.ExecuteScalarAsync<long>(new(P,new{Action=A(ProcedureAction.Insert),e.ProjectId,e.TaskId,e.IssueId,e.FileName,e.FilePath,e.ContentType,e.FileSizeKb,e.UploadedByUserId,User=e.InsertedBy},commandType:CommandType.StoredProcedure,cancellationToken:ct));}
 public async Task<bool>DeleteAsync(long id,long? user,CancellationToken ct=default){await using var c=f.CreateConnection();return await c.ExecuteScalarAsync<long>(new(P,new{Action=A(ProcedureAction.Delete),Id=id,User=user},commandType:CommandType.StoredProcedure,cancellationToken:ct))>0;}
}
