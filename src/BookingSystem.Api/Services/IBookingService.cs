using BookingSystem.Api.DTOs;

namespace BookingSystem.Api.Services;

public interface IBookingService
{
    Task<BookingResult> CreateBookingAsync(string userId, int timeSlotId, CancellationToken ct = default);
    Task<bool> CancelBookingAsync(int bookingId, string userId, bool isAdmin, CancellationToken ct = default);
}
