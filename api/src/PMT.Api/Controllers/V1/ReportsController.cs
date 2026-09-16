using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PMT.Application.Reports;
using PMT.Application.Reports.Dtos;
using PMT.Application.Common.Security;
namespace PMT.Api.Controllers.V1;
[Authorize(Policy=PermissionRequirement.ReportsView)]
[ApiController, Route("api/v1/reports"), EnableRateLimiting("api")]
public sealed class ReportsController(ReportService service) : ControllerBase
{
    [HttpGet("velocity")] public Task<IReadOnlyCollection<VelocityDto>> Velocity([FromQuery]long? projectId,[FromQuery]DateTime from,[FromQuery]DateTime to,CancellationToken cancellationToken) => service.GetVelocityAsync(projectId,from,to,cancellationToken);
    [HttpGet("workload")] public Task<IReadOnlyCollection<WorkloadDto>> Workload([FromQuery]long? projectId,CancellationToken cancellationToken) => service.GetWorkloadAsync(projectId,cancellationToken);
}
