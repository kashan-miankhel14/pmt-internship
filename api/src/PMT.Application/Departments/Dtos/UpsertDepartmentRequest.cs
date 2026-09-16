namespace PMT.Application.Departments.Dtos;

public sealed record UpsertDepartmentRequest(string Code, string Name, string? Description, bool Active = true);
