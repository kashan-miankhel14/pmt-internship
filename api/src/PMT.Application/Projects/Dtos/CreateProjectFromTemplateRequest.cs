namespace PMT.Application.Projects.Dtos;

public sealed record CreateProjectFromTemplateRequest(
    string Key,
    string Name,
    string? Description,
    string TypeCode,
    string AccessLevel,
    long LeadUserId);
