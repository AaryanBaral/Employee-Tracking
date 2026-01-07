using Microsoft.EntityFrameworkCore;
using Tracker.Api.Models;

namespace Tracker.Api.Data;

public class TrackerDbContext : DbContext
{
    public TrackerDbContext(DbContextOptions<TrackerDbContext> options)
        : base(options)
    {
    }

    public DbSet<Device> Devices => Set<Device>();
    public DbSet<IngestCursor> IngestCursors => Set<IngestCursor>();
    public DbSet<WebEvent> WebEvents => Set<WebEvent>();
    public DbSet<WebSession> WebSessions => Set<WebSession>();
    public DbSet<AppSession> AppSessions => Set<AppSession>();
    public DbSet<IdleSession> IdleSessions => Set<IdleSession>();
    public DbSet<IdleSecondsRow> IdleSecondsRows => Set<IdleSecondsRow>();
    public DbSet<DomainSummaryRow> DomainSummaryRows => Set<DomainSummaryRow>();
    public DbSet<AppSummaryRow> AppSummaryRows => Set<AppSummaryRow>();
    public DbSet<UrlSummaryRow> UrlSummaryRows => Set<UrlSummaryRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Device>()
            .HasIndex(d => d.LastSeenAt);

        modelBuilder.Entity<IngestCursor>()
            .HasKey(c => new { c.DeviceId, c.Stream });

        modelBuilder.Entity<WebEvent>()
            .HasIndex(e => e.EventId)
            .IsUnique();
        modelBuilder.Entity<WebEvent>()
            .HasIndex(e => new { e.DeviceId, e.Timestamp });
        modelBuilder.Entity<WebEvent>()
            .Property(e => e.EventId)
            .IsRequired();
        modelBuilder.Entity<WebEvent>()
            .Property(e => e.DeviceId)
            .IsRequired()
            .HasMaxLength(64);
        modelBuilder.Entity<WebEvent>()
            .Property(e => e.Domain)
            .IsRequired()
            .HasMaxLength(255);
        modelBuilder.Entity<WebEvent>()
            .Property(e => e.Title)
            .HasMaxLength(512);
        modelBuilder.Entity<WebEvent>()
            .Property(e => e.Url)
            .HasMaxLength(2048);
        modelBuilder.Entity<WebEvent>()
            .Property(e => e.Browser)
            .HasMaxLength(64);
        modelBuilder.Entity<WebEvent>()
            .Property(e => e.ReceivedAt)
            .IsRequired();

        modelBuilder.Entity<WebSession>()
            .HasIndex(s => new { s.DeviceId, s.StartAt });
        modelBuilder.Entity<WebSession>()
            .HasIndex(s => new { s.DeviceId, s.EndAt });
        modelBuilder.Entity<WebSession>()
            .HasIndex(s => s.SessionId)
            .IsUnique();
        modelBuilder.Entity<WebSession>()
            .Property(s => s.SessionId)
            .IsRequired();
        modelBuilder.Entity<WebSession>()
            .Property(s => s.Url)
            .HasMaxLength(2048);
        modelBuilder.Entity<WebSession>()
            .Property(s => s.Domain)
            .IsRequired()
            .HasMaxLength(255);
        modelBuilder.Entity<WebSession>()
            .Property(s => s.Title)
            .HasMaxLength(512);

        modelBuilder.Entity<AppSession>()
            .HasIndex(s => new { s.DeviceId, s.StartAt });
        modelBuilder.Entity<AppSession>()
            .HasIndex(s => new { s.DeviceId, s.EndAt });
        modelBuilder.Entity<AppSession>()
            .HasIndex(s => s.SessionId)
            .IsUnique();
        modelBuilder.Entity<AppSession>()
            .Property(s => s.SessionId)
            .IsRequired();

        modelBuilder.Entity<IdleSession>()
            .HasIndex(s => new { s.DeviceId, s.StartAt });
        modelBuilder.Entity<IdleSession>()
            .HasIndex(s => new { s.DeviceId, s.EndAt });
        modelBuilder.Entity<IdleSession>()
            .HasIndex(s => s.SessionId)
            .IsUnique();
        modelBuilder.Entity<IdleSession>()
            .Property(s => s.SessionId)
            .IsRequired();

        modelBuilder.Entity<IdleSecondsRow>().HasNoKey();
        modelBuilder.Entity<DomainSummaryRow>().HasNoKey();
        modelBuilder.Entity<AppSummaryRow>().HasNoKey();
        modelBuilder.Entity<UrlSummaryRow>().HasNoKey();
    }
}
