using Microsoft.EntityFrameworkCore;
using Shine.Domain.Identity;

namespace Shine.Infrastructure.Persistence;

public sealed class ShineDbContext(DbContextOptions<ShineDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<UserTenant> UserTenants => Set<UserTenant>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(user => user.Id);
            entity.Property(user => user.Email).HasMaxLength(320).IsRequired();
            entity.Property(user => user.NormalizedEmail).HasMaxLength(320).IsRequired();
            entity.Property(user => user.PasswordHash).IsRequired();
            entity.HasIndex(user => user.NormalizedEmail).IsUnique();
        });

        modelBuilder.Entity<PasswordResetToken>(entity =>
        {
            entity.HasKey(token => token.Id);
            entity.Property(token => token.TokenHash).HasMaxLength(128).IsRequired();
            entity.HasIndex(token => token.TokenHash).IsUnique();
            entity.HasOne(token => token.User).WithMany().HasForeignKey(token => token.UserId);
        });

        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasKey(tenant => tenant.Id);
            entity.Property(tenant => tenant.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(tenant => tenant.Name).IsUnique();
        });

        modelBuilder.Entity<UserTenant>(entity =>
        {
            entity.HasKey(link => new { link.UserId, link.TenantId });
            entity.HasOne(link => link.User).WithMany(user => user.Tenants).HasForeignKey(link => link.UserId);
            entity.HasOne(link => link.Tenant).WithMany(tenant => tenant.Users).HasForeignKey(link => link.TenantId);
            entity.HasIndex(link => new { link.TenantId, link.UserId }).IsUnique();
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(token => token.Id);
            entity.Property(token => token.TokenHash).HasMaxLength(128).IsRequired();
            entity.HasIndex(token => token.TokenHash).IsUnique();
            entity.HasOne(token => token.User).WithMany().HasForeignKey(token => token.UserId);
        });
    }
}
