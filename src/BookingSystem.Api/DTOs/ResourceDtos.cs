namespace BookingSystem.Api.DTOs;

public record CreateResourceRequest(string Name, string? Description, string? Location, int Capacity);

public record ResourceResponse(int Id, string Name, string? Description, string? Location, int Capacity);

public record CreateSlotsRequest(DateTime StartUtc, DateTime EndUtc, int SlotLengthMinutes);

public record TimeSlotResponse(int Id, int ResourceId, DateTime StartUtc, DateTime EndUtc, bool IsBooked);
