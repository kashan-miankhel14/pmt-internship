using PMT.Domain.Common;


namespace PMT.Domain.Entities;

public sealed class User : AuditableEntity
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public long RoleId { get; set; }
    public long DepartmentId { get; set; }
    public string UserName { get => Email; set { } }
    public string DisplayName { get => FullName; set => FullName = value; }
    public bool IsLocked { get; set; }
    public int FailedLoginAttempts { get; set; }
    public DateTime? LockoutEnd { get; set; }
    public DateTime? LastLoginDate { get; set; }
}
