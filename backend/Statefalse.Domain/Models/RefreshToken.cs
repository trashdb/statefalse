using System.ComponentModel.DataAnnotations;

namespace Statefalse.Domain.Models;

public sealed class RefreshToken
{
    [Key]
    public int Id { get; set; }

    public long GitHubId { get; set; }

    [Required]
    [MaxLength(64)]
    public string FamilyId { get; set; } = string.Empty;

    [Required]
    [MaxLength(64)]
    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime FamilyExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public DateTime? UsedAt { get; set; }

    public DateTime? ReuseDetectedAt { get; set; }

    [MaxLength(64)]
    public string? ReplacedByTokenHash { get; set; }

    [MaxLength(64)]
    public string? ParentTokenHash { get; set; }
}
