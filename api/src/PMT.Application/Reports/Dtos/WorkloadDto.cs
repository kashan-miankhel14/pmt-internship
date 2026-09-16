namespace PMT.Application.Reports.Dtos;
public sealed record WorkloadDto(long? UserId, string UserName, int OpenTasks, int OpenIssues, decimal EstimatedHours);
