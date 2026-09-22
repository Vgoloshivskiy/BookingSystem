using System.Security.Claims;
using BookingSystem.Api.Data;
using BookingSystem.Api.DTOs;
using BookingSystem.Api.Models;
using BookingSystem.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BookingSystem.Api.Controllers;

[ApiController]
[Route("api/bookings")]
[Authorize]
public class BookingsController : ControllerBase
{
    private readonly IBookingService _bookingService;
    private readonly ApplicationDbContext _db;

    public BookingsController(IBookingService bookingService, ApplicationDbContext db)
    {
        _bookingService = bookingService;
        _db = db;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue("sub")
        ?? throw new InvalidOperationException("No user id claim on request.");

    /// <summary>
    /// Book a slot. Returns 201 on success, 409 Conflict if another request won the race
    /// for this slot first, 404 if the slot doesn't exist. Never a 500 for the "someone
    /// else booked it first" case — that is an expected, everyday outcome, not a server error.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<BookingResponse>> Create(CreateBookingRequest request)
    {
        var result = await _bookingService.CreateBookingAsync(CurrentUserId, request.TimeSlotId);

        return result.Outcome switch
        {
            BookingOutcome.Created => CreatedAtAction(nameof(GetMine), new { }, result.Booking),
            BookingOutcome.SlotNotFound => NotFound(new { message = "Time slot not found." }),
            BookingOutcome.AlreadyBooked => Conflict(new { message = "This slot was just booked by someone else." }),
            _ => StatusCode(500)
        };
    }

    [HttpGet("mine")]
    public async Task<ActionResult<IEnumerable<BookingResponse>>> GetMine()
    {
        var bookings = await QueryBookingResponses(_db.Bookings.Where(b => b.UserId == CurrentUserId));
        return Ok(bookings);
    }

    [HttpGet]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<IEnumerable<BookingResponse>>> GetAll()
    {
        var bookings = await QueryBookingResponses(_db.Bookings);
        return Ok(bookings);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Cancel(int id)
    {
        var isAdmin = User.IsInRole(Roles.Admin);
        var success = await _bookingService.CancelBookingAsync(id, CurrentUserId, isAdmin);
        return success ? NoContent() : NotFound();
    }

    private async Task<List<BookingResponse>> QueryBookingResponses(IQueryable<Booking> source)
    {
        return await source.AsNoTracking()
            .Include(b => b.TimeSlot).ThenInclude(s => s!.Resource)
            .Include(b => b.User)
            .OrderByDescending(b => b.CreatedAtUtc)
            .Select(b => new BookingResponse(
                b.Id, b.TimeSlotId, b.TimeSlot!.ResourceId, b.TimeSlot.Resource!.Name,
                b.TimeSlot.StartUtc, b.TimeSlot.EndUtc, b.UserId, b.User!.DisplayName, b.CreatedAtUtc))
            .ToListAsync();
    }
}
