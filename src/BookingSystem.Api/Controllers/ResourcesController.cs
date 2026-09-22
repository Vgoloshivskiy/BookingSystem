using BookingSystem.Api.Data;
using BookingSystem.Api.DTOs;
using BookingSystem.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BookingSystem.Api.Controllers;

[ApiController]
[Route("api/resources")]
[Authorize]
public class ResourcesController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public ResourcesController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ResourceResponse>>> GetAll()
    {
        var resources = await _db.Resources.AsNoTracking()
            .Select(r => new ResourceResponse(r.Id, r.Name, r.Description, r.Location, r.Capacity))
            .ToListAsync();
        return Ok(resources);
    }

    [HttpGet("{id:int}/slots")]
    public async Task<ActionResult<IEnumerable<TimeSlotResponse>>> GetSlots(int id, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var query = _db.TimeSlots.AsNoTracking().Where(s => s.ResourceId == id);
        if (from.HasValue) query = query.Where(s => s.StartUtc >= from.Value);
        if (to.HasValue) query = query.Where(s => s.EndUtc <= to.Value);

        var slots = await query.OrderBy(s => s.StartUtc)
            .Select(s => new TimeSlotResponse(s.Id, s.ResourceId, s.StartUtc, s.EndUtc, s.IsBooked))
            .ToListAsync();
        return Ok(slots);
    }

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<ResourceResponse>> Create(CreateResourceRequest request)
    {
        var resource = new Resource
        {
            Name = request.Name,
            Description = request.Description,
            Location = request.Location,
            Capacity = request.Capacity
        };
        _db.Resources.Add(resource);
        await _db.SaveChangesAsync();

        var response = new ResourceResponse(resource.Id, resource.Name, resource.Description, resource.Location, resource.Capacity);
        return CreatedAtAction(nameof(GetAll), new { id = resource.Id }, response);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Update(int id, CreateResourceRequest request)
    {
        var resource = await _db.Resources.FindAsync(id);
        if (resource is null) return NotFound();

        resource.Name = request.Name;
        resource.Description = request.Description;
        resource.Location = request.Location;
        resource.Capacity = request.Capacity;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Delete(int id)
    {
        var resource = await _db.Resources.FindAsync(id);
        if (resource is null) return NotFound();

        _db.Resources.Remove(resource);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:int}/slots")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<IEnumerable<TimeSlotResponse>>> CreateSlots(int id, CreateSlotsRequest request)
    {
        var resource = await _db.Resources.FindAsync(id);
        if (resource is null) return NotFound();

        var slots = new List<TimeSlot>();
        for (var start = request.StartUtc; start.AddMinutes(request.SlotLengthMinutes) <= request.EndUtc; start = start.AddMinutes(request.SlotLengthMinutes))
        {
            slots.Add(new TimeSlot
            {
                ResourceId = id,
                StartUtc = start,
                EndUtc = start.AddMinutes(request.SlotLengthMinutes),
                IsBooked = false
            });
        }

        _db.TimeSlots.AddRange(slots);
        await _db.SaveChangesAsync();

        return Ok(slots.Select(s => new TimeSlotResponse(s.Id, s.ResourceId, s.StartUtc, s.EndUtc, s.IsBooked)));
    }
}
