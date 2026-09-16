namespace PMT.Application.Departments.Dtos;

public sealed record DepartmentDto(long Id, string Code, string Name, string? Description, bool Active);
