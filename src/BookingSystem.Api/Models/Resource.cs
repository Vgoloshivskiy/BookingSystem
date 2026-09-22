namespace BookingSystem.Api.Models;

/// <summary>
/// A bookable resource, e.g. a meeting room. Admins manage the catalogue of
/// resources and the time slots that belong to each one.
/// </summary>
public class Resource
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Location { get; set; }
    public int Capacity { get; set; }

    public List<TimeSlot> TimeSlots { get; set; } = new();
}
