# CLAUDE.md

This file configures Claude Code for this repository. It's the entry point Claude reads
before making changes here, so it captures the project's architecture, conventions, and
the non-negotiable design decisions — most importantly, the concurrency-control approach,
which must never be "simplified" back into a check-then-insert race condition.

## Project

ASP.NET Core 8 meeting-room booking system with real-time updates. Solution layout:

- `src/BookingSystem.Api` — the API: controllers, EF Core data access, Identity/JWT auth,
  the SignalR hub, and `BookingService` (the concurrency-critical booking logic).
- `tests/BookingSystem.Tests` — xUnit tests, including the required automated
  concurrency test in `ConcurrencyTests.cs`.
- `frontend/` — a dependency-free vanilla JS/HTML/CSS SPA (no build step) that talks to
  the API over REST and to the SignalR hub for live slot updates.
- `docs/CONCURRENCY.md` — full write-up of the concurrency design decision.

## Non-negotiable design decisions

1. **Booking a slot is one atomic `UPDATE ... WHERE IsBooked = 0` statement**, executed
   inside a transaction, followed by inserting the `Booking` row. It is never a
   "SELECT to check, then INSERT" sequence — that pattern is a race condition and is
   explicitly disallowed by the assignment. See `BookingService.CreateBookingAsync` and
   `docs/CONCURRENCY.md`.
2. There is a **unique database index on `Bookings.TimeSlotId`** as a defense-in-depth
   safety net, independent of the application-level logic above.
3. The booking endpoint returns **409 Conflict** (never a 500, never a silent
   overwrite) when a slot was already taken by a concurrent request.
4. SignalR broadcasts happen **only after the database transaction commits** — never
   optimistically before the write is durable.
5. Any change to `BookingService.CreateBookingAsync` must keep (or improve) the
   `ConcurrencyTests.ConcurrentBookingRequests_ForSameSlot_ExactlyOneSucceeds` test
   green. That test is the acceptance criterion for the whole feature.

## Conventions

- Nullable reference types are enabled; don't suppress warnings with `!` unless the
  non-null invariant is genuinely guaranteed (and say why in a comment).
- Controllers stay thin: validation + calling a service + mapping to a DTO. Business
  logic (especially anything touching concurrency) lives in `Services/`.
- DTOs are C# `record` types, one file per aggregate (`BookingDtos.cs`, etc.), never the
  EF entities themselves — entities are never returned directly from an endpoint.
- New endpoints that mutate state should have at least one test in
  `BookingSystem.Tests` covering the happy path and the conflict/error path.

## Commands

```bash
# Restore & build
dotnet restore
dotnet build

# Run the API locally (uses local SQLite by default, see appsettings.json)
dotnet run --project src/BookingSystem.Api

# Run all tests, including the concurrency test
dotnet test

# Add an EF Core migration (do this once before relying on Migrate/database update -
# see the note in Data/SeedData.cs; the repo currently uses EnsureCreatedAsync()
# because no migration files have been generated yet)
dotnet ef migrations add <Name> --project src/BookingSystem.Api

# Apply migrations against Azure SQL (set Database:Provider=SqlServer and the
# ConnectionStrings:SqlServer app setting first; also switch SeedData.cs from
# EnsureCreatedAsync() to MigrateAsync() once migrations exist)
dotnet ef database update --project src/BookingSystem.Api
```

## Development process note

This repository was built with active use of Claude Code, per the assignment's item 9.
Commit messages describe what changed and why; see `git log` for the sequence in which
the system was assembled (models → data access → concurrency-safe booking service →
API surface → real-time layer → frontend → tests → deployment docs).
