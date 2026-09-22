using BookingSystem.Api.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BookingSystem.Api.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<TimeSlot> TimeSlots => Set<TimeSlot>();
    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Resource>(e =>
        {
            e.Property(r => r.Name).IsRequired().HasMaxLength(200);
            e.HasMany(r => r.TimeSlots)
                .WithOne(s => s.Resource!)
                .HasForeignKey(s => s.ResourceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TimeSlot>(e =>
        {
            e.HasIndex(s => new { s.ResourceId, s.StartUtc }).IsUnique();

            e.HasOne(s => s.Booking)
                .WithOne(b => b.TimeSlot!)
                .HasForeignKey<Booking>(b => b.TimeSlotId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Booking>(e =>
        {
            // Database-enforced safety net for the concurrency-control design: a slot can
            // appear in the Bookings table at most once, full stop. See docs/CONCURRENCY.md.
            e.HasIndex(b => b.TimeSlotId).IsUnique();

            e.HasOne(b => b.User)
                .WithMany()
                .HasForeignKey(b => b.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
