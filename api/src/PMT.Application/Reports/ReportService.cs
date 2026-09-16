using System.Globalization;
using PMT.Application.Common.Caching;
using PMT.Application.Reports.Dtos;
namespace PMT.Application.Reports;

/// <summary>
/// Velocity and workload are aggregate scans over stories/tasks. They are re-requested on
/// every dashboard render but their inputs only move when work items change, so results are
/// held for <see cref="CacheRegions.ReportsTtl"/>.
/// </summary>
/// <remarks>
/// There is no write-path invalidation here on purpose: the reports aggregate almost every
/// table, so a correct invalidation would have to fire from every mutation in the system. The
/// 60-second TTL is the staleness contract instead.
/// </remarks>
public sealed class ReportService(IReportRepository repository, ILookupCache cache)
{
    public Task<IReadOnlyCollection<VelocityDto>> GetVelocityAsync(long? projectId, DateTime from, DateTime to, CancellationToken cancellationToken = default)
        => cache.GetOrCreateAsync(
            CacheRegions.Reports,
            string.Create(CultureInfo.InvariantCulture, $"velocity:{Scope(projectId)}:{from.Ticks}:{to.Ticks}"),
            CacheRegions.ReportsTtl,
            ct => repository.GetVelocityAsync(projectId, from, to, ct),
            cancellationToken);

    public Task<IReadOnlyCollection<WorkloadDto>> GetWorkloadAsync(long? projectId, CancellationToken cancellationToken = default)
        => cache.GetOrCreateAsync(
            CacheRegions.Reports,
            string.Create(CultureInfo.InvariantCulture, $"workload:{Scope(projectId)}"),
            CacheRegions.ReportsTtl,
            ct => repository.GetWorkloadAsync(projectId, ct),
            cancellationToken);

    private static string Scope(long? projectId) =>
        projectId?.ToString(CultureInfo.InvariantCulture) ?? "all";
}
