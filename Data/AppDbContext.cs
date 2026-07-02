using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Api.Models;

namespace Api.Data;

// IDataProtectionKeyContext: the container's filesystem doesn't survive a rebuild/redeploy,
// so the Data Protection key ring (which encrypts email-confirmation and password-reset
// tokens) needs somewhere durable to live — Postgres, since it's already here. Wired up in
// Program.cs via .PersistKeysToDbContext<AppDbContext>().
public class AppDbContext : IdentityDbContext<User, IdentityRole<Guid>, Guid>, IDataProtectionKeyContext
{
  public AppDbContext(DbContextOptions<AppDbContext> options)
      : base(options)
  {
  }

  protected override void OnModelCreating(ModelBuilder builder)
  {
    base.OnModelCreating(builder);

    builder.Entity<User>(b =>
    {
      b.Property(u => u.Status).HasConversion<string>().HasMaxLength(20);
      b.HasQueryFilter(u => !u.IsDeleted);
    });

    builder.Entity<RefreshToken>(rt =>
    {
      rt.HasKey(t => t.Id);

      rt.Property(t => t.TokenHash).HasMaxLength(128).IsRequired();

      rt.HasIndex(t => t.TokenHash).IsUnique(); // every lookup is by hash
      rt.HasIndex(t => t.FamilyId);             // family-wide revocation
      rt.HasIndex(t => t.UserId);               // revoke-all-for-user

      rt.HasOne(t => t.User)
        .WithMany() // no collection nav on User — keep the entity lean
        .HasForeignKey(t => t.UserId)
        .OnDelete(DeleteBehavior.Cascade);
    });

    builder.Entity<AuditEvent>(ae =>
    {
      ae.HasKey(e => e.Id);
      ae.Property(e => e.EventType).HasConversion<string>().HasMaxLength(50);
      ae.Property(e => e.IpAddress).HasMaxLength(64);
      ae.Property(e => e.UserAgent).HasMaxLength(512);
      ae.Property(e => e.Detail).HasMaxLength(1024);

      ae.HasIndex(e => e.UserId);
      ae.HasIndex(e => e.CreatedAtUtc);
      ae.HasIndex(e => e.EventType);
    });

    builder.Entity<Category>(ct =>
    {
      ct.HasIndex(c => new { c.UserId, c.Name }).IsUnique();
      ct.Property(c => c.Name).HasMaxLength(100).IsRequired();
    });

    builder.Entity<Transaction>(tx =>
    {
      tx.Property(t => t.Type).HasConversion<string>().HasMaxLength(20);
      tx.Property(t => t.Amount).HasPrecision(18, 2);
      tx.Property(t => t.Notes).HasMaxLength(500);

      tx.HasIndex(t => new { t.UserId, t.Date });

      tx.HasOne(t => t.Category)
        .WithMany()
        .HasForeignKey(t => t.CategoryId)
        .OnDelete(DeleteBehavior.Restrict);
    });

    builder.Entity<RecurringTransaction>(rt =>
    {
      rt.Property(t => t.Type).HasConversion<string>().HasMaxLength(20);
      rt.Property(t => t.Frequency).HasConversion<string>().HasMaxLength(20);
      rt.Property(t => t.Amount).HasPrecision(18, 2);
      rt.Property(t => t.Notes).HasMaxLength(500);

      rt.HasIndex(t => new { t.UserId, t.NextDueDate });

      rt.HasOne(t => t.Category)
        .WithMany()
        .HasForeignKey(t => t.CategoryId)
        .OnDelete(DeleteBehavior.Restrict);
    });
  }

  public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
  public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
  public DbSet<Category> Categories => Set<Category>();
  public DbSet<Transaction> Transactions => Set<Transaction>();
  public DbSet<RecurringTransaction> RecurringTransactions => Set<RecurringTransaction>();
  public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
}