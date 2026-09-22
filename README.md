# Meeting Room Booking System

A booking system for a limited set of resources (meeting rooms) where multiple users
may race to book the same slot. The system guarantees a slot is never double-booked
and reflects booking status to all viewers in real time.

## Stack

- **Backend:** ASP.NET Core 8 Web API, EF Core, ASP.NET Core Identity + JWT auth
- **Real-time:** SignalR (Azure SignalR Service in production, local in-process
  SignalR for dev/tests)
- **Database:** Azure SQL in production, SQLite for local dev and the automated test
- **Frontend:** dependency-free vanilla JS/HTML/CSS (no build tooling required)

## Repository layout

```
src/BookingSystem.Api/     API: controllers, EF Core, Identity/JWT, SignalR hub,
                            BookingService (the concurrency-critical logic)
tests/BookingSystem.Tests/ xUnit tests, including the required concurrency test
frontend/                  Static SPA (index.html / app.js / styles.css)
docs/CONCURRENCY.md        Concurrency design decision, written up in full
docs/DEPLOYMENT.md         Step-by-step Azure provisioning + deploy
CLAUDE.md                  Claude Code configuration for this repo
.claude/skills/            A custom skill for adding new endpoints consistently
```

## Roles

- **User:** view resources and their schedules; book available slots; view/cancel own
  bookings.
- **Admin:** everything a User can do, plus create/edit/delete resources and their
  slots, and view all bookings across all users.

A seed admin account is created on first run: `admin@bookingsystem.local` /
`Admin123!`. Change or remove it before a real deployment (see `docs/DEPLOYMENT.md`).

## Concurrency control — the short version

Booking a slot is a single atomic `UPDATE TimeSlots SET IsBooked = 1 WHERE Id = @id
AND IsBooked = 0` statement inside a transaction, not a separate check-then-insert.
Exactly one concurrent request can ever affect a row; every other concurrent request
affects zero rows and gets `409 Conflict`. A unique index on `Bookings.TimeSlotId`
backs this up as a defense-in-depth safety net. Full rationale, alternatives
considered, and why they were rejected: **`docs/CONCURRENCY.md`**.

## Running locally

Requires the .NET 8 SDK.

```bash
# Restore & build
dotnet restore
dotnet build

# Run the API (defaults to a local SQLite file, no Azure resources needed)
dotnet run --project src/BookingSystem.Api
# → Swagger UI at http://localhost:5000/swagger (or the port dotnet prints)

# Serve the frontend (any static file server works, e.g.)
npx serve frontend
# or just open frontend/index.html directly; set window.API_BASE_URL in the
# browser console first if the API isn't on http://localhost:5000
```

## Running the automated concurrency test

```bash
dotnet test
```

`ConcurrencyTests.ConcurrentBookingRequests_ForSameSlot_ExactlyOneSucceeds` boots the
real API in-process, creates one resource with one time slot, and fires 20 concurrent
authenticated booking requests at it, then asserts exactly one `201 Created`, the rest
`409 Conflict`, zero server errors, and exactly one booking row in the database
afterwards.

## Deploying to Azure

See **`docs/DEPLOYMENT.md`** for the full `az` CLI walkthrough (SQL Database, SignalR
Service, Web App, app settings, publish).

## Development process

Built with active use of Claude Code; see `CLAUDE.md` for the project's conventions
and non-negotiable design decisions, and `git log` for the commit-by-commit build
sequence (models → data access → concurrency-safe booking service → API surface →
real-time layer → frontend → tests → deployment docs).
