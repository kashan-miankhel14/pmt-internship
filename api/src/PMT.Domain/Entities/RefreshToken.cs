using PMT.Domain.Common;


namespace PMT.Domain.Entities;

public sealed class RefreshToken : AuditableEntity
{
    public long UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiryDate { get; set; }
    public bool IsRevoked { get; set; }
    public string JwtId { get; set; } = string.Empty;
    public DateTime ExpiresAt { get => ExpiryDate; set => ExpiryDate = value; }
    public DateTime? RevokedAt { get => IsRevoked ? UpdateDate ?? DateTime.MinValue : null; set => IsRevoked = value.HasValue; }
    public string? ReplacedByTokenHash { get; set; }
    public string? CreatedByIp { get; set; }
    public string? RevokedByIp { get; set; }
}
