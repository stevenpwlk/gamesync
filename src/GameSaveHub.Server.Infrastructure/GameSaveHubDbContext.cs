using Microsoft.EntityFrameworkCore;

namespace GameSaveHub.Server.Infrastructure;

public sealed class GameSaveHubDbContext(DbContextOptions<GameSaveHubDbContext> options) : DbContext(options)
{
    public DbSet<WorldEntity> Worlds => Set<WorldEntity>();
    public DbSet<DeviceEntity> Devices => Set<DeviceEntity>();
    public DbSet<EnrollmentEntity> Enrollments => Set<EnrollmentEntity>();
    public DbSet<AuthChallengeEntity> AuthChallenges => Set<AuthChallengeEntity>();
    public DbSet<SessionEntity> Sessions => Set<SessionEntity>();
    public DbSet<SaveVersionEntity> SaveVersions => Set<SaveVersionEntity>();
    public DbSet<UploadEntity> Uploads => Set<UploadEntity>();
    public DbSet<UploadChunkEntity> UploadChunks => Set<UploadChunkEntity>();
    public DbSet<IdempotencyEntity> Idempotency => Set<IdempotencyEntity>();
    public DbSet<AdminAuditEntity> AdminAudit => Set<AdminAuditEntity>();
    public DbSet<ClientReleaseEntity> ClientReleases => Set<ClientReleaseEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorldEntity>().HasIndex(x => x.Name).IsUnique();
        modelBuilder.Entity<DeviceEntity>().HasIndex(x => x.Name);
        modelBuilder.Entity<EnrollmentEntity>().HasIndex(x => x.CodeHash).IsUnique();
        modelBuilder.Entity<AuthChallengeEntity>().HasIndex(x => new { x.DeviceId, x.ExpiresAtUtc });
        modelBuilder.Entity<SessionEntity>().HasIndex(x => x.WorldId)
            .IsUnique()
            .HasFilter("ReleasedAtUtc IS NULL");
        modelBuilder.Entity<SessionEntity>().Property(x => x.State).HasConversion<string>();
        modelBuilder.Entity<SaveVersionEntity>().HasIndex(x => x.Sha256);
        modelBuilder.Entity<SaveVersionEntity>().HasIndex(x => new { x.WorldId, x.CreatedAtUtc });
        modelBuilder.Entity<UploadChunkEntity>().HasKey(x => new { x.UploadId, x.Index });
        modelBuilder.Entity<IdempotencyEntity>().HasKey(x => new { x.DeviceId, x.Key, x.Route });
        modelBuilder.Entity<AdminAuditEntity>().HasIndex(x => x.PerformedAtUtc);
        modelBuilder.Entity<ClientReleaseEntity>().HasIndex(x => x.Version).IsUnique();
        modelBuilder.Entity<ClientReleaseEntity>().HasIndex(x => x.PublishedAtUtc);

        // SQLite ne sait ni ordonner ni comparer DateTimeOffset. Toutes les valeurs sont UTC et stockées en entier.
        modelBuilder.Entity<WorldEntity>().Property(x => x.CreatedAtUtc).HasConversion<long>();
        modelBuilder.Entity<DeviceEntity>().Property(x => x.EnrolledAtUtc).HasConversion<long>();
        modelBuilder.Entity<DeviceEntity>().Property(x => x.RevokedAtUtc).HasConversion<long?>();
        modelBuilder.Entity<EnrollmentEntity>().Property(x => x.ExpiresAtUtc).HasConversion<long>();
        modelBuilder.Entity<EnrollmentEntity>().Property(x => x.RedeemedAtUtc).HasConversion<long?>();
        modelBuilder.Entity<AuthChallengeEntity>().Property(x => x.ExpiresAtUtc).HasConversion<long>();
        modelBuilder.Entity<AuthChallengeEntity>().Property(x => x.UsedAtUtc).HasConversion<long?>();
        modelBuilder.Entity<SessionEntity>().Property(x => x.CreatedAtUtc).HasConversion<long>();
        modelBuilder.Entity<SessionEntity>().Property(x => x.LastHeartbeatAtUtc).HasConversion<long>();
        modelBuilder.Entity<SessionEntity>().Property(x => x.ReleasedAtUtc).HasConversion<long?>();
        modelBuilder.Entity<SaveVersionEntity>().Property(x => x.CreatedAtUtc).HasConversion<long>();
        modelBuilder.Entity<UploadEntity>().Property(x => x.CreatedAtUtc).HasConversion<long>();
        modelBuilder.Entity<UploadEntity>().Property(x => x.CommittedAtUtc).HasConversion<long?>();
        modelBuilder.Entity<UploadChunkEntity>().Property(x => x.ReceivedAtUtc).HasConversion<long>();
        modelBuilder.Entity<IdempotencyEntity>().Property(x => x.CreatedAtUtc).HasConversion<long>();
        modelBuilder.Entity<AdminAuditEntity>().Property(x => x.PerformedAtUtc).HasConversion<long>();
        modelBuilder.Entity<ClientReleaseEntity>().Property(x => x.PublishedAtUtc).HasConversion<long>();
    }
}
