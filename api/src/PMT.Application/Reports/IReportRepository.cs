using PMT.Application.Reports.Dtos;
namespace PMT.Application.Reports;
public interface IReportRepository
{
    Task<IReadOnlyCollection<VelocityDto>> GetVelocityAsync(long? projectId, DateTime from, DateTime to, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<WorkloadDto>> GetWorkloadAsync(long? projectId, CancellationToken cancellationToken = default);
}
