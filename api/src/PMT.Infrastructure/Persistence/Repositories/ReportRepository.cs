using System.Data;using Dapper;using PMT.Application.Common.Interfaces;using PMT.Application.Reports;using PMT.Application.Reports.Dtos;using PMT.Domain.Enums;
namespace PMT.Infrastructure.Persistence.Repositories;
public sealed class ReportRepository(IDbConnectionFactory f):IReportRepository
{
 static string P=>ProcedureNames.Get(StoredProcedure.Report);static string A(ProcedureAction a)=>ProcedureNames.Action(a);
 public async Task<IReadOnlyCollection<VelocityDto>>GetVelocityAsync(long? projectId,DateTime from,DateTime to,CancellationToken ct=default){await using var c=f.CreateConnection();return(await c.QueryAsync<VelocityDto>(new(P,new{Action=A(ProcedureAction.Velocity),ProjectId=projectId,From=from,To=to},commandType:CommandType.StoredProcedure,cancellationToken:ct))).AsList();}
 public async Task<IReadOnlyCollection<WorkloadDto>>GetWorkloadAsync(long? projectId,CancellationToken ct=default){await using var c=f.CreateConnection();return(await c.QueryAsync<WorkloadDto>(new(P,new{Action=A(ProcedureAction.Workload),ProjectId=projectId},commandType:CommandType.StoredProcedure,cancellationToken:ct))).AsList();}
}
