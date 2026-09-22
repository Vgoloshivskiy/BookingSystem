namespace BookingSystem.Api.DTOs;

public record CreateBookingRequest(int TimeSlotId);

public record BookingResponse(
    int Id,
    int TimeSlotId,
    int ResourceId,
    string ResourceName,
    DateTime StartUtc,
    DateTime EndUtc,
    string UserId,
    string UserDisplayName,
    DateTime CreatedAtUtc);

/// <summary>
/// The outcome of a booking attempt. Booking() is intentionally NOT a plain bool/throw
/// API: the caller (controller) needs to distinguish "created" from "conflict" from
/// "not found" without relying on exception-driven control flow for the expected,
/// everyday case of losing a race to another user.
/// </summary>
public enum BookingOutcome
{
    Created,
    SlotNotFound,
    AlreadyBooked
}

public record BookingResult(BookingOutcome Outcome, BookingResponse? Booking);
