using System.ComponentModel.DataAnnotations;

namespace Statefalse.Domain.Models;

public class Notification
{
    [Key]
    public int Id { get; set; }

    public long RecipientGitHubId { get; set; }

    [Required, MaxLength(50)]
    public string Kind { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required, MaxLength(1000)]
    public string Body { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Repo { get; set; }

    public long? PrNumber { get; set; }

    [MaxLength(500)]
    public string? PrUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsRead { get; set; }
}

