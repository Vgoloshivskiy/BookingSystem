namespace BookingSystem.Api.Models;

/// <summary>
/// A single bookable window of time on a resource. <see cref="IsBooked"/> is the
/// field the concurrency-safe booking flow flips atomically (see BookingService) —
/// it is never set by a "read state, decide, write state" pattern from application code.
/// </summary>
public class TimeSlot
{
    public int Id { get; set; }
    public int ResourceId { get; set; }
    public Resource? Resource { get; set; }

    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }

    public bool IsBooked { get; set; }

    public Booking? Booking { get; set; }
}
