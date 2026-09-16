using PMT.Domain.Enums;
namespace PMT.Application.GitLinks.Dtos;
public sealed record GitLinkDto(long Id, string EntityType, long EntityId, GitProvider Provider, string RepositoryUrl, string ReferenceType, string ReferenceId, string? ReferenceUrl);
