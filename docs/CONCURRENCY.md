# Concurrency design decision

## The requirement

If two (or more) requests to book the same time slot arrive at effectively the same
time, exactly one must succeed and the rest must receive a clear conflict response
(HTTP 409) — never a silent overwrite, never a server error. A naive "check if free,
then insert" implemented as two separate, unprotected steps does not satisfy this.

## Why "check, then insert" is unsafe

```
SELECT IsBooked FROM TimeSlots WHERE Id = @id;   -- both requests read false
-- both requests conclude the slot is free
INSERT INTO Bookings (...);                       -- both requests insert
```

Between the `SELECT` and the `INSERT`, there is a window during which a second
transaction can run the same `SELECT` and reach the same conclusion. This is a
classic time-of-check-to-time-of-use (TOCTOU) race. Under load, or with two requests
arriving within microseconds of each other, both can pass the check before either
writes — resulting in two bookings for one slot, exactly the double-booking the
system must prevent.

## The chosen mechanism: atomic conditional update

`BookingService.CreateBookingAsync` does this instead:

```sql
UPDATE TimeSlots SET IsBooked = 1 WHERE Id = @id AND IsBooked = 0;
```

This single statement **is** the check and the write — there is no window between
them for another transaction to interleave. The database evaluates `WHERE Id = @id
AND IsBooked = 0` and applies the update to that row as one indivisible operation.
Executed inside a transaction:

- If the slot is currently free, exactly one concurrent request's `UPDATE` will
  match the row and set `IsBooked = 1`; the row-level lock the database takes to
  perform that update forces every other concurrent `UPDATE` targeting the same row
  to wait, and once it proceeds, `IsBooked` is no longer `0`, so it matches zero rows.
- The application inspects the affected-row count returned by `ExecuteSqlInterpolatedAsync`.
  `0` unambiguously means "someone else got there first" → the service returns
  `BookingOutcome.AlreadyBooked` → the controller returns **409 Conflict**.
  `1` means this request won the race → it proceeds to insert the `Booking` row and
  commits.

This was chosen over two alternatives that were considered:

- **Optimistic concurrency via a `RowVersion`/`[Timestamp]` column.** This works well
  for update-heavy scenarios but is awkward here because SQL Server's `rowversion`
  type has no equivalent in SQLite (used for the automated test and local dev),
  which would mean the concurrency mechanism itself differed between test and
  production — undermining the point of having an automated test for it. The plain
  `UPDATE ... WHERE` approach is portable, ANSI SQL, and identical on both providers.
- **Application-level locks (e.g. a `SemaphoreSlim` keyed by slot id).** This only
  protects against races between requests handled by the *same process*. On Azure,
  the API can and will scale to multiple instances, and an in-memory lock provides
  no protection at all across instances. A database-level guarantee is required
  because the database is the only thing all instances share.

## Defense in depth: a unique index

`Bookings.TimeSlotId` also has a unique index (configured in `ApplicationDbContext`).
This should be unreachable in normal operation — the conditional `UPDATE` above
already prevents two bookings for one slot — but it means that even a future code
change that bypasses `BookingService` (a bug, a new admin tool, a direct SQL script)
still cannot physically create two `Booking` rows for the same slot. `BookingService`
catches the resulting `DbUpdateException` and also maps it to `AlreadyBooked`/409,
so the guarantee holds at the API boundary even in that scenario.

## Isolation level and transactions

The transaction uses `IsolationLevel.Serializable`. Combined with the conditional
`UPDATE`, this ensures the read-modify-write is fully isolated from other
concurrently-committing transactions touching the same row, and EF Core's execution
strategy (`CreateExecutionStrategy`) wraps the whole transaction so transient faults
(e.g. an Azure SQL failover) are retried safely without partially applying the change.

## How this is verified automatically

`tests/BookingSystem.Tests/ConcurrencyTests.cs` boots the real API (via
`WebApplicationFactory`) against a real file-backed SQLite database, creates one
resource with a single time slot, and fires 20 concurrent authenticated booking
requests for that same slot with `Task.WhenAll`. It asserts:

- Exactly 1 response is `201 Created`.
- Exactly 19 responses are `409 Conflict`.
- Zero responses are a 5xx server error.
- Querying all bookings afterwards shows exactly one booking row for that slot.

Run it with `dotnet test`.
