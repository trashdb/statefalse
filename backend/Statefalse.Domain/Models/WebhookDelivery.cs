using System.ComponentModel.DataAnnotations;

namespace Statefalse.Domain.Models;

public class WebhookDelivery
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string DeliveryId { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string EventType { get; set; } = string.Empty;

    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ProcessedAt { get; set; }
}
