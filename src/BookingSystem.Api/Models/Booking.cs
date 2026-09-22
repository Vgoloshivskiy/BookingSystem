namespace BookingSystem.Api.Models;

/// <summary>
/// A confirmed booking of a single time slot by a single user. The unique index on
/// <see cref="TimeSlotId"/> (configured in ApplicationDbContext) is a database-enforced
/// safety net: even if application-level concurrency control were ever bypassed, the
/// database itself physically cannot hold two bookings for the same slot.
/// </summary>
public class Booking
{
    public int Id { get; set; }

    public int TimeSlotId { get; set; }
    public TimeSlot? TimeSlot { get; set; }

    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
