using System.Data;using Dapper;using PMT.Application.Common.Interfaces;using PMT.Application.Notifications;using PMT.Domain.Entities;using PMT.Domain.Enums;
namespace PMT.Infrastructure.Persistence.Repositories;
public sealed class NotificationRepository(IDbConnectionFactory f):INotificationRepository
{
 static string P=>ProcedureNames.Get(StoredProcedure.Notification);static string A(ProcedureAction a)=>ProcedureNames.Action(a);
 public async Task<IReadOnlyCollection<Notification>>GetForUserAsync(long userId,bool unreadOnly,CancellationToken ct=default){await using var c=f.CreateConnection();return(await c.QueryAsync<Notification>(new(P,new{Action=A(ProcedureAction.Fetch),UserId=userId,UnreadOnly=unreadOnly},commandType:CommandType.StoredProcedure,cancellationToken:ct))).AsList();}
 // Title and Link are columns on dbo.Notification and parameters of SP_NOTIFICATION's INSERT
 // branch; omitting them here is what used to store every notification with a null headline and
 // no target, so the stored row disagreed with the SignalR payload the recipient had just seen.
 public async Task<long>CreateAsync(Notification e,CancellationToken ct=default){await using var c=f.CreateConnection();return await c.ExecuteScalarAsync<long>(new(P,new{Action=A(ProcedureAction.Insert),e.UserId,e.EventType,e.Title,e.Message,e.Link,e.IsRead,User=e.InsertedBy},commandType:CommandType.StoredProcedure,cancellationToken:ct));}
 public async Task<bool>MarkReadAsync(long id,long userId,CancellationToken ct=default){await using var c=f.CreateConnection();return await c.ExecuteScalarAsync<long>(new(P,new{Action=A(ProcedureAction.MarkRead),Id=id,UserId=userId},commandType:CommandType.StoredProcedure,cancellationToken:ct))>0;}
}
