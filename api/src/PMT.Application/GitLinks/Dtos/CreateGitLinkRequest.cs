using PMT.Domain.Enums;
namespace PMT.Application.GitLinks.Dtos;
public sealed record CreateGitLinkRequest(string EntityType, long EntityId, GitProvider Provider, string RepositoryUrl, string ReferenceType, string ReferenceId, string? ReferenceUrl);
