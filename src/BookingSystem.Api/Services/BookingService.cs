using System.Data;
using BookingSystem.Api.Data;
using BookingSystem.Api.DTOs;
using BookingSystem.Api.Hubs;
using BookingSystem.Api.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BookingSystem.Api.Services;

/// <summary>
/// Owns the one operation in this system that must never race: turning a free slot into
/// a booked one. See docs/CONCURRENCY.md for the full design rationale; the short version
/// is documented inline at the point of the atomic update below.
/// </summary>
public class BookingService : IBookingService
{
    private readonly ApplicationDbContext _db;
    private readonly IHubContext<BookingHub> _hub;

    public BookingService(ApplicationDbContext db, IHubContext<BookingHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    public async Task<BookingResult> CreateBookingAsync(string userId, int timeSlotId, CancellationToken ct = default)
    {
        // EF Core's execution strategy handles transient-fault retries (e.g. Azure SQL
        // failover). It must wrap the whole transaction, not individual calls.
        var strategy = _db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            var slot = await _db.TimeSlots.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == timeSlotId, ct);

            if (slot is null)
            {
                await transaction.RollbackAsync(ct);
                return new BookingResult(BookingOutcome.SlotNotFound, null);
            }

            // --- The concurrency-control mechanism -----------------------------------
            // This is a single atomic UPDATE ... WHERE statement, not a separate
            // "read IsBooked, decide in application code, then write" sequence. The
            // database evaluates the WHERE clause and applies the write as one
            // indivisible operation per row. If two requests for the same slot arrive
            // at effectively the same time, the database's own row-level locking
            // guarantees only one of the two UPDATE statements can actually match a row
            // with IsBooked = 0 — the loser's statement affects zero rows, deterministically,
            // even under full concurrency. That is what "affected == 0" below detects.
            // This is deliberate: a plain "SELECT IsBooked; if false, INSERT booking" would
            // let two concurrent requests both read "false" before either writes, and both
            // would proceed to insert a booking (a classic TOCTOU race). This design
            // avoids that window entirely by making the check and the write one operation.
            var affected = await _db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE TimeSlots SET IsBooked = 1 WHERE Id = {timeSlotId} AND IsBooked = 0", ct);

            if (affected == 0)
            {
                await transaction.RollbackAsync(ct);
                return new BookingResult(BookingOutcome.AlreadyBooked, null);
            }

            var booking = new Booking
            {
                TimeSlotId = timeSlotId,
                UserId = userId,
                CreatedAtUtc = DateTime.UtcNow
            };
            _db.Bookings.Add(booking);

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Should be unreachable given the conditional UPDATE above — this is the
                // unique-index safety net (see ApplicationDbContext) catching it anyway.
                await transaction.RollbackAsync(ct);
                return new BookingResult(BookingOutcome.AlreadyBooked, null);
            }

            await transaction.CommitAsync(ct);

            var resourceName = await _db.Resources.AsNoTracking()
                .Where(r => r.Id == slot.ResourceId)
                .Select(r => r.Name)
                .FirstAsync(ct);
            var displayName = await _db.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => u.DisplayName)
                .FirstAsync(ct);

            var response = new BookingResponse(
                booking.Id, timeSlotId, slot.ResourceId, resourceName,
                slot.StartUtc, slot.EndUtc, userId, displayName, booking.CreatedAtUtc);

            // Broadcast only after the transaction has committed, so every viewer's
            // real-time update reflects a fact that is now durably true in the database.
            await BookingHub.BroadcastSlotStatusChanged(_hub, slot.ResourceId, timeSlotId, isBooked: true);

            return new BookingResult(BookingOutcome.Created, response);
        });
    }

    public async Task<bool> CancelBookingAsync(int bookingId, string userId, bool isAdmin, CancellationToken ct = default)
    {
        var strategy = _db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);

            var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId, ct);
            if (booking is null || (!isAdmin && booking.UserId != userId))
            {
                await transaction.RollbackAsync(ct);
                return false;
            }

            var timeSlotId = booking.TimeSlotId;
            _db.Bookings.Remove(booking);
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE TimeSlots SET IsBooked = 0 WHERE Id = {timeSlotId}", ct);
            await _db.SaveChangesAsync(ct);

            var resourceId = await _db.TimeSlots.AsNoTracking()
                .Where(s => s.Id == timeSlotId).Select(s => s.ResourceId).FirstAsync(ct);

            await transaction.CommitAsync(ct);

            await BookingHub.BroadcastSlotStatusChanged(_hub, resourceId, timeSlotId, isBooked: false);
            return true;
        });
    }
}
