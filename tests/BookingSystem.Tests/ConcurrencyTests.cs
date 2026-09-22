using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BookingSystem.Api.DTOs;
using Xunit;

namespace BookingSystem.Tests;

/// <summary>
/// Item 6 of the assignment: an automated test that fires multiple simultaneous booking
/// requests at the same slot and asserts that exactly one booking is created.
///
/// This test does not call BookingService directly — it goes through the real HTTP
/// pipeline (WebApplicationFactory + HttpClient), so it also exercises auth, the
/// controller, and EF Core's real SQLite connection handling. That's deliberate: it
/// proves the guarantee end-to-end, not just at the unit level.
/// </summary>
public class ConcurrencyTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ConcurrencyTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ConcurrentBookingRequests_ForSameSlot_ExactlyOneSucceeds()
    {
        const int concurrentRequests = 20;

        // Arrange: one admin-created resource + slot, and N distinct registered users
        // who will all race to book it.
        var adminClient = await CreateAuthenticatedUserClientAsync(
            "admin@bookingsystem.local", password: "Admin123!", registerIfMissing: false);
        var (resourceId, timeSlotId) = await CreateResourceWithOneSlotAsync(adminClient);

        var userClients = new List<HttpClient>();
        for (var i = 0; i < concurrentRequests; i++)
        {
            userClients.Add(await CreateAuthenticatedUserClientAsync($"racer{i}-{Guid.NewGuid():N}@test.local"));
        }

        // Act: fire all booking requests for the SAME slot at effectively the same time.
        var bookingTasks = userClients
            .Select(client => client.PostAsJsonAsync("/api/bookings", new CreateBookingRequest(timeSlotId)))
            .ToArray();

        var responses = await Task.WhenAll(bookingTasks);

        // Assert: exactly one request succeeded (201 Created); every other request got a
        // clear conflict response (409) — never a silent overwrite, never a 5xx error.
        var succeeded = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var conflicted = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);
        var serverErrors = responses.Where(r => (int)r.StatusCode >= 500).ToList();

        Assert.True(serverErrors.Count == 0,
            $"Expected zero server errors, got {serverErrors.Count}: " +
            string.Join(", ", serverErrors.Select(r => r.StatusCode)));
        Assert.Equal(1, succeeded);
        Assert.Equal(concurrentRequests - 1, conflicted);

        // Cross-check directly against the source of truth: the admin's view of all
        // bookings must contain exactly one row for this slot, not zero and not several.
        var allBookings = await adminClient.GetFromJsonAsync<List<BookingResponse>>("/api/bookings");
        var bookingsForSlot = allBookings!.Count(b => b.TimeSlotId == timeSlotId);
        Assert.Equal(1, bookingsForSlot);
    }

    private async Task<(int resourceId, int timeSlotId)> CreateResourceWithOneSlotAsync(HttpClient adminClient)
    {
        var resourceResponse = await adminClient.PostAsJsonAsync("/api/resources",
            new CreateResourceRequest("Race Room", "Used by the concurrency test", "Test Floor", 4));
        resourceResponse.EnsureSuccessStatusCode();
        var resource = await resourceResponse.Content.ReadFromJsonAsync<ResourceResponse>();

        var start = DateTime.UtcNow.AddDays(10);
        var slotsResponse = await adminClient.PostAsJsonAsync($"/api/resources/{resource!.Id}/slots",
            new CreateSlotsRequest(start, start.AddMinutes(30), 30));
        slotsResponse.EnsureSuccessStatusCode();
        var slots = await slotsResponse.Content.ReadFromJsonAsync<List<TimeSlotResponse>>();

        return (resource.Id, slots!.Single().Id);
    }

    private async Task<HttpClient> CreateAuthenticatedUserClientAsync(
        string email, string password = "Password123!", bool registerIfMissing = true)
    {
        var client = _factory.CreateClient();

        if (registerIfMissing)
        {
            await client.PostAsJsonAsync("/api/auth/register",
                new RegisterRequest(email, password, DisplayName: email));
        }

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        loginResponse.EnsureSuccessStatusCode();
        var auth = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        return client;
    }
}
