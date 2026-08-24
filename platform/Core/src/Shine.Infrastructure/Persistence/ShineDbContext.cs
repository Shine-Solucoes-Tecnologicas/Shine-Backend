using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Shine.Domain;
using Shine.Domain.Identity;
using Shine.Domain.Authorization;
using Microsoft.AspNetCore.Http;

namespace Shine.Infrastructure.Persistence;

public sealed class ShineDbContext(
    DbContextOptions<ShineDbContext> options,
    ICurrentUser? currentUser = null,
    ICurrentTenant? currentTenant = null,
    ITenantExecutionContext? tenantExecutionContext = null,
    IHttpContextAccessor? httpContextAccessor = null,
    IClock? clock = null,
    IDomainEventDispatcher? domainEventDispatcher = null) : DbContext(options)
{
    private Guid? EffectiveTenantId => tenantExecutionContext?.EffectiveTenantId ?? currentTenant?.TenantId;
    public DbSet<User> Users => Set<User>();
    public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<CustomerAccount> CustomerAccounts => Set<CustomerAccount>();
    public DbSet<CustomerAccountUser> CustomerAccountUsers => Set<CustomerAccountUser>();
    public DbSet<CustomerAccountRole> CustomerAccountRoles => Set<CustomerAccountRole>();
    public DbSet<CustomerAccountRolePermission> CustomerAccountRolePermissions => Set<CustomerAccountRolePermission>();
    public DbSet<CustomerAccountUserRole> CustomerAccountUserRoles => Set<CustomerAccountUserRole>();
    public DbSet<CustomerAccountUserRoleUnit> CustomerAccountUserRoleUnits => Set<CustomerAccountUserRoleUnit>();
    public DbSet<CustomerAccountUserRoleModule> CustomerAccountUserRoleModules => Set<CustomerAccountUserRoleModule>();
    public DbSet<UserTenant> UserTenants => Set<UserTenant>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<OperationalLog> OperationalLogs => Set<OperationalLog>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationReadReceipt> NotificationReadReceipts => Set<NotificationReadReceipt>();
    public DbSet<FunctionalSetting> FunctionalSettings => Set<FunctionalSetting>();
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();
    public DbSet<ModuleAccess> ModuleAccesses => Set<ModuleAccess>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<PlanModule> PlanModules => Set<PlanModule>();
    public DbSet<PlanEntitlement> PlanEntitlements => Set<PlanEntitlement>();
    public DbSet<TenantPlan> TenantPlans => Set<TenantPlan>();
    public DbSet<TenantModuleOverride> TenantModuleOverrides => Set<TenantModuleOverride>();
    public DbSet<TenantEntitlementOverride> TenantEntitlementOverrides => Set<TenantEntitlementOverride>();
    public DbSet<EntitlementUsage> EntitlementUsages => Set<EntitlementUsage>();
    public DbSet<EntitlementReservation> EntitlementReservations => Set<EntitlementReservation>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserTenantRole> UserTenantRoles => Set<UserTenantRole>();
    public DbSet<GlobalRole> GlobalRoles => Set<GlobalRole>();
    public DbSet<GlobalRolePermission> GlobalRolePermissions => Set<GlobalRolePermission>();
    public DbSet<UserGlobalRole> UserGlobalRoles => Set<UserGlobalRole>();
    public DbSet<DashboardLayout> DashboardLayouts => Set<DashboardLayout>();
    public DbSet<DashboardWidgetPlacement> DashboardWidgetPlacements => Set<DashboardWidgetPlacement>();
    public DbSet<StoredFileMetadata> StoredFiles => Set<StoredFileMetadata>();
    public DbSet<Customer> Customers => Set<Customer>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        var aggregates = PendingAggregates();
        ApplyAuditMetadata();
        var result = base.SaveChanges(acceptAllChangesOnSuccess);
        PublishEvents(aggregates);
        return result;
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var aggregates = PendingAggregates();
        ApplyAuditMetadata();
        return SaveAndPublishAsync(aggregates, acceptAllChangesOnSuccess, cancellationToken);
    }

    private async Task<int> SaveAndPublishAsync(IReadOnlyCollection<IHasDomainEvents> aggregates, bool acceptAllChangesOnSuccess, CancellationToken cancellationToken)
    {
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        await PublishEventsAsync(aggregates, cancellationToken);
        return result;
    }

    private IReadOnlyCollection<IHasDomainEvents> PendingAggregates() => ChangeTracker.Entries()
        .Select(entry => entry.Entity)
        .OfType<IHasDomainEvents>()
        .Where(aggregate => aggregate.DomainEvents.Count > 0)
        .Distinct()
        .ToArray();

    private void PublishEvents(IReadOnlyCollection<IHasDomainEvents> aggregates)
    {
        if (domainEventDispatcher is null || aggregates.Count == 0) return;
        domainEventDispatcher.PublishAsync(aggregates).GetAwaiter().GetResult();
    }

    private Task PublishEventsAsync(IReadOnlyCollection<IHasDomainEvents> aggregates, CancellationToken cancellationToken) =>
        domainEventDispatcher is null || aggregates.Count == 0
            ? Task.CompletedTask
            : domainEventDispatcher.PublishAsync(aggregates, cancellationToken);

    private void ApplyAuditMetadata()
    {
        var userId = currentUser?.UserId;
        var tenantId = EffectiveTenantId;
        var now = clock?.UtcNow ?? DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries()
                     .Where(entry => entry.Metadata.ClrType != typeof(AuditEntry))
                     .ToArray())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            if (entry.State == EntityState.Deleted && entry.Entity is ISoftDeletable softDeletable)
            {
                softDeletable.Delete(now);
                entry.State = EntityState.Modified;
            }

            ApplyTenantBoundary(entry);

            var action = entry.State switch
            {
                EntityState.Added => "CREATE",
                EntityState.Modified => "UPDATE",
                _ => "DELETE"
            };

            if (entry.Entity is AuditableEntity auditable)
            {
                if (entry.State == EntityState.Added)
                    auditable.MarkCreated(userId, tenantId, now);
                else if (entry.State == EntityState.Modified)
                    auditable.MarkUpdated(userId, now);
            }

            var key = entry.Metadata.FindPrimaryKey()?.Properties
                .Select(property => entry.Property(property.Name).CurrentValue?.ToString())
                .Where(value => value is not null)
                .ToArray();
            var entityId = key is { Length: > 0 } ? string.Join("/", key) : "pending";
            var entityType = entry.Metadata.ClrType.Name;
            var values = entry.Properties.ToDictionary(property => property.Metadata.Name,
                property => MaskSensitive(entityType, property.Metadata.Name, property.CurrentValue));
            var oldValues = entry.State == EntityState.Added ? null : entry.Properties.ToDictionary(property => property.Metadata.Name,
                property => MaskSensitive(entityType, property.Metadata.Name, property.OriginalValue));
            var httpContext = httpContextAccessor?.HttpContext;

            AuditEntries.Add(AuditEntry.Create(
                entityType,
                entityId,
                action,
                userId,
                tenantId,
                now,
                httpContext?.TraceIdentifier,
                httpContext?.Connection.RemoteIpAddress?.ToString(),
                httpContext?.Request.Headers.UserAgent.ToString(),
                oldValues is null ? null : JsonSerializer.Serialize(oldValues),
                JsonSerializer.Serialize(values)));
        }
    }

    private void ApplyTenantBoundary(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
    {
        if (entry.Entity is not IMultiTenantEntity)
            return;

        if (tenantExecutionContext?.IsBypass == true)
            return;

        if (EffectiveTenantId is not Guid tenantId)
            throw new TenantIsolationException("An active tenant or an explicit bypass is required for multi-tenant writes.");

        var tenantProperty = entry.Metadata.FindProperty(nameof(IMultiTenantEntity.TenantId));
        if (tenantProperty is null)
            return;

        var currentValue = entry.Property(tenantProperty.Name).CurrentValue;
        if (currentValue is Guid existingTenant && existingTenant != Guid.Empty && existingTenant != tenantId)
            throw new TenantIsolationException("The entity belongs to another tenant.");

        if (entry.State == EntityState.Added)
            entry.Property(tenantProperty.Name).CurrentValue = tenantId;
        else if (currentValue is Guid existing && existing != tenantId)
            throw new TenantIsolationException("The entity belongs to another tenant.");
    }

    private static object? MaskSensitive(string entityType, string propertyName, object? value)
    {
        if (value is null) return null;

        var sensitive = propertyName.Contains("password", StringComparison.OrdinalIgnoreCase)
            || propertyName.Contains("token", StringComparison.OrdinalIgnoreCase)
            || propertyName.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || propertyName.Contains("credential", StringComparison.OrdinalIgnoreCase)
            || propertyName.Contains("privatekey", StringComparison.OrdinalIgnoreCase)
            || propertyName.Contains("email", StringComparison.OrdinalIgnoreCase)
            || propertyName.Contains("phone", StringComparison.OrdinalIgnoreCase)
            || propertyName.Contains("taxidentifier", StringComparison.OrdinalIgnoreCase)
            || propertyName.Contains("document", StringComparison.OrdinalIgnoreCase)
            || entityType == nameof(Customer) && propertyName is nameof(Customer.Name) or nameof(Customer.NormalizedName);

        return sensitive || value is string text && ContainsSensitiveValue(text) ? "[MASKED]" : value;
    }

    private static bool ContainsSensitiveValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        var normalized = new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        var credentialMarkers = new[]
        {
            "password", "secret", "credential", "privatekey", "connectionstring",
            "accesstoken", "refreshtoken", "cardtoken", "paymenttoken", "cardnumber", "cvv", "cvc"
        };
        if (credentialMarkers.Any(normalized.Contains)) return true;

        var at = value.IndexOf('@');
        if (at > 0 && at < value.Length - 3 && value[(at + 1)..].Contains('.')) return true;

        return value.Count(char.IsDigit) >= 10;
    }

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

        modelBuilder.Entity<EmailVerificationToken>(entity =>
        {
            entity.HasKey(token => token.Id);
            entity.Property(token => token.TokenHash).HasMaxLength(128).IsRequired();
            entity.HasIndex(token => token.TokenHash).IsUnique();
            entity.HasIndex(token => new { token.UserId, token.UsedAtUtc });
            entity.HasOne(token => token.User).WithMany().HasForeignKey(token => token.UserId);
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
            entity.HasOne(tenant => tenant.CustomerAccount).WithMany(account => account.Tenants).HasForeignKey(tenant => tenant.CustomerAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(tenant => tenant.CustomerAccountId);
        });

        modelBuilder.Entity<CustomerAccount>(entity =>
        {
            entity.HasKey(account => account.Id);
            entity.Property(account => account.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(account => account.Name);
        });
        modelBuilder.Entity<CustomerAccountUser>(entity =>
        {
            entity.HasKey(link => new { link.AccountId, link.UserId });
            entity.HasOne(link => link.Account).WithMany().HasForeignKey(link => link.AccountId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(link => link.User).WithMany().HasForeignKey(link => link.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(link => link.UserId);
        });
        modelBuilder.Entity<CustomerAccountRole>(entity =>
        {
            entity.HasKey(role => role.Id);
            entity.Property(role => role.Name).HasMaxLength(80).IsRequired();
            entity.HasAlternateKey(role => new { role.AccountId, role.Id });
            entity.HasIndex(role => new { role.AccountId, role.Name }).IsUnique();
            entity.HasOne(role => role.Account).WithMany().HasForeignKey(role => role.AccountId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<CustomerAccountRolePermission>(entity =>
        {
            entity.HasKey(link => new { link.RoleId, link.PermissionId });
            entity.HasOne(link => link.Role).WithMany(role => role.Permissions).HasForeignKey(link => link.RoleId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(link => link.Permission).WithMany().HasForeignKey(link => link.PermissionId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<CustomerAccountUserRole>(entity =>
        {
            entity.HasKey(link => new { link.AccountId, link.UserId, link.RoleId });
            entity.HasOne(link => link.Membership).WithMany(member => member.Roles).HasForeignKey(link => new { link.AccountId, link.UserId }).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(link => link.Role).WithMany().HasForeignKey(link => new { link.AccountId, link.RoleId }).HasPrincipalKey(role => new { role.AccountId, role.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(link => new { link.UserId, link.RoleId });
        });
        modelBuilder.Entity<CustomerAccountUserRoleUnit>(entity =>
        {
            entity.HasKey(scope => new { scope.AccountId, scope.UserId, scope.RoleId, scope.UnitId });
            entity.HasOne(scope => scope.Assignment).WithMany(link => link.Units).HasForeignKey(scope => new { scope.AccountId, scope.UserId, scope.RoleId }).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(scope => scope.Unit).WithMany().HasForeignKey(scope => scope.UnitId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(scope => scope.UnitId);
        });
        modelBuilder.Entity<CustomerAccountUserRoleModule>(entity =>
        {
            entity.HasKey(scope => new { scope.AccountId, scope.UserId, scope.RoleId, scope.ModuleCode });
            entity.Property(scope => scope.ModuleCode).HasMaxLength(120).IsRequired();
            entity.HasOne(scope => scope.Assignment).WithMany(link => link.Modules).HasForeignKey(scope => new { scope.AccountId, scope.UserId, scope.RoleId }).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserTenant>(entity =>
        {
            entity.HasKey(link => new { link.UserId, link.TenantId });
            entity.Property(link => link.UserTenantId).IsRequired();
            entity.HasIndex(link => link.UserTenantId).IsUnique();
            entity.HasOne(link => link.User).WithMany(user => user.Tenants).HasForeignKey(link => link.UserId);
            entity.HasOne(link => link.Tenant).WithMany(tenant => tenant.Users).HasForeignKey(link => link.TenantId);
            entity.HasIndex(link => new { link.TenantId, link.UserId }).IsUnique();
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(token => token.Id);
            entity.Property(token => token.TokenHash).HasMaxLength(128).IsRequired();
            entity.HasIndex(token => token.UserTenantId);
            entity.HasIndex(token => token.TokenHash).IsUnique();
            entity.HasOne(token => token.User).WithMany().HasForeignKey(token => token.UserId);
        });

        modelBuilder.Entity<Permission>(entity =>
        {
            entity.HasKey(permission => permission.Id);
            entity.Property(permission => permission.Code).HasMaxLength(120).IsRequired();
            entity.Property(permission => permission.Description).HasMaxLength(300).IsRequired();
            entity.HasIndex(permission => permission.Code).IsUnique();
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(role => role.Id);
            entity.Property(role => role.Name).HasMaxLength(120).IsRequired();
            entity.HasIndex(role => new { role.TenantId, role.Name }).IsUnique();
            entity.HasOne(role => role.Tenant).WithMany().HasForeignKey(role => role.TenantId);
            entity.HasQueryFilter(role => tenantExecutionContext != null && tenantExecutionContext.IsBypass ||
                EffectiveTenantId != null && role.TenantId == EffectiveTenantId);
        });

        modelBuilder.Entity<RolePermission>(entity =>
        {
            entity.HasKey(link => new { link.RoleId, link.PermissionId });
            entity.HasOne(link => link.Role).WithMany(role => role.Permissions).HasForeignKey(link => link.RoleId);
            entity.HasOne(link => link.Permission).WithMany(permission => permission.Roles).HasForeignKey(link => link.PermissionId);
        });

        modelBuilder.Entity<UserTenantRole>(entity =>
        {
            entity.HasKey(link => new { link.UserId, link.TenantId, link.RoleId });
            entity.HasOne(link => link.User).WithMany().HasForeignKey(link => link.UserId);
            entity.HasOne(link => link.TenantMembership).WithMany().HasForeignKey(link => new { link.UserId, link.TenantId });
            entity.HasOne(link => link.Role).WithMany(role => role.Users).HasForeignKey(link => link.RoleId);
            entity.HasQueryFilter(link => tenantExecutionContext != null && tenantExecutionContext.IsBypass ||
                EffectiveTenantId != null && link.TenantId == EffectiveTenantId);
        });

        modelBuilder.Entity<GlobalRole>(entity =>
        {
            entity.HasKey(role => role.Id);
            entity.Property(role => role.Name).HasMaxLength(120).IsRequired();
            entity.HasIndex(role => role.Name).IsUnique();
        });

        modelBuilder.Entity<GlobalRolePermission>(entity =>
        {
            entity.HasKey(link => new { link.RoleId, link.PermissionId });
            entity.HasOne(link => link.Role).WithMany(role => role.Permissions).HasForeignKey(link => link.RoleId);
            entity.HasOne(link => link.Permission).WithMany().HasForeignKey(link => link.PermissionId);
        });

        modelBuilder.Entity<UserGlobalRole>(entity =>
        {
            entity.HasKey(link => new { link.UserId, link.RoleId });
            entity.HasOne(link => link.User).WithMany().HasForeignKey(link => link.UserId);
            entity.HasOne(link => link.Role).WithMany(role => role.Users).HasForeignKey(link => link.RoleId);
        });

        modelBuilder.Entity<AuditEntry>(entity =>
        {
            entity.HasKey(entry => entry.Id);
            entity.Property(entry => entry.EntityType).HasMaxLength(200).IsRequired();
            entity.Property(entry => entry.EntityId).HasMaxLength(256).IsRequired();
            entity.Property(entry => entry.Action).HasMaxLength(30).IsRequired();
            entity.Property(entry => entry.CorrelationId).HasMaxLength(100);
            entity.Property(entry => entry.IpAddress).HasMaxLength(64);
            entity.Property(entry => entry.UserAgent).HasMaxLength(512);
            entity.Property(entry => entry.OldValuesJson).HasColumnType("jsonb");
            entity.Property(entry => entry.NewValuesJson).HasColumnType("jsonb");
            entity.HasIndex(entry => new { entry.TenantId, entry.EntityType, entry.EntityId });
            entity.HasIndex(entry => entry.OccurredAtUtc);
        });

        modelBuilder.Entity<OperationalLog>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Level).HasMaxLength(32).IsRequired();
            entity.Property(item => item.Category).HasMaxLength(256).IsRequired();
            entity.Property(item => item.Message).HasMaxLength(4000).IsRequired();
            entity.Property(item => item.Exception).HasMaxLength(12000);
            entity.Property(item => item.CorrelationId).HasMaxLength(100);
            entity.Property(item => item.TraceId).HasMaxLength(100);
            entity.HasIndex(item => item.CreatedAtUtc);
            entity.HasIndex(item => new { item.Level, item.Category });
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(notification => notification.Id);
            entity.Property(notification => notification.Type).HasMaxLength(80).IsRequired();
            entity.Property(notification => notification.Title).HasMaxLength(200).IsRequired();
            entity.Property(notification => notification.Message).HasMaxLength(2000).IsRequired();
            entity.Property(notification => notification.DataJson).HasColumnType("jsonb");
            entity.HasIndex(notification => new { notification.TenantId, notification.RecipientUserId, notification.ReadAtUtc });
            entity.HasQueryFilter(notification => !notification.IsDeleted &&
                (tenantExecutionContext != null && tenantExecutionContext.IsBypass ||
                 EffectiveTenantId != null && notification.TenantId == EffectiveTenantId));
        });

        modelBuilder.Entity<NotificationReadReceipt>(entity =>
        {
            entity.HasKey(x => new { x.NotificationId, x.UserId });
            entity.HasOne<Notification>().WithMany().HasForeignKey(x => x.NotificationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.TenantId, x.UserId, x.ReadAtUtc });
            entity.HasQueryFilter(x => tenantExecutionContext != null && tenantExecutionContext.IsBypass ||
                EffectiveTenantId != null && x.TenantId == EffectiveTenantId);
        });

        modelBuilder.Entity<FunctionalSetting>(entity =>
        {
            entity.HasKey(setting => setting.Id);
            entity.Property(setting => setting.Key).HasMaxLength(120).IsRequired();
            entity.Property(setting => setting.Value).HasMaxLength(4000).IsRequired();
            entity.HasIndex(setting => new { setting.TenantId, setting.Key }).IsUnique();
            entity.HasQueryFilter(setting => !setting.IsDeleted &&
                (tenantExecutionContext != null && tenantExecutionContext.IsBypass || setting.TenantId == null ||
                 EffectiveTenantId != null && setting.TenantId == EffectiveTenantId));
        });

        modelBuilder.Entity<FeatureFlag>(entity =>
        {
            entity.HasKey(flag => flag.Id);
            entity.Property(flag => flag.Key).HasMaxLength(120).IsRequired();
            entity.HasIndex(flag => new { flag.TenantId, flag.Key }).IsUnique();
            entity.HasQueryFilter(flag => !flag.IsDeleted &&
                (tenantExecutionContext != null && tenantExecutionContext.IsBypass || flag.TenantId == null ||
                 EffectiveTenantId != null && flag.TenantId == EffectiveTenantId));
        });

        modelBuilder.Entity<ModuleAccess>(entity =>
        {
            entity.HasKey(access => access.Id);
            entity.Property(access => access.ModuleCode).HasMaxLength(120).IsRequired();
            entity.HasIndex(access => new { access.TenantId, access.ModuleCode }).IsUnique();
            entity.HasQueryFilter(access => !access.IsDeleted && (tenantExecutionContext != null && tenantExecutionContext.IsBypass ||
                EffectiveTenantId != null && access.TenantId == EffectiveTenantId));
        });

        modelBuilder.Entity<Plan>(entity => { entity.HasKey(x => x.Id); entity.Property(x => x.Code).HasMaxLength(120).IsRequired(); entity.Property(x => x.Name).HasMaxLength(200).IsRequired(); entity.HasIndex(x => x.Code).IsUnique(); entity.HasQueryFilter(x => !x.IsDeleted); });
        modelBuilder.Entity<PlanModule>(entity => { entity.HasKey(x => new { x.PlanId, x.ModuleCode }); entity.Property(x => x.ModuleCode).HasMaxLength(120).IsRequired(); entity.HasOne<Plan>().WithMany(x => x.Modules).HasForeignKey(x => x.PlanId); });
        modelBuilder.Entity<PlanEntitlement>(entity => { entity.HasKey(x => new { x.PlanId, x.Key }); entity.Property(x => x.Key).HasMaxLength(120).IsRequired(); entity.Property(x => x.State).HasConversion<int>(); entity.Property(x => x.Version).HasDefaultValue(1); entity.Property(x => x.IsUnlimited).HasDefaultValue(false); entity.HasOne<Plan>().WithMany().HasForeignKey(x => x.PlanId); });
        modelBuilder.Entity<TenantPlan>(entity => { entity.HasKey(x => x.Id); entity.HasIndex(x => x.TenantId).IsUnique(); entity.HasQueryFilter(x => !x.IsDeleted && (tenantExecutionContext != null && tenantExecutionContext.IsBypass || EffectiveTenantId != null && x.TenantId == EffectiveTenantId)); });
        modelBuilder.Entity<TenantModuleOverride>(entity => { entity.HasKey(x => x.Id); entity.Property(x => x.ModuleCode).HasMaxLength(120).IsRequired(); entity.HasIndex(x => new { x.TenantId, x.ModuleCode }).IsUnique(); entity.HasQueryFilter(x => !x.IsDeleted && (tenantExecutionContext != null && tenantExecutionContext.IsBypass || EffectiveTenantId != null && x.TenantId == EffectiveTenantId)); });
        modelBuilder.Entity<TenantEntitlementOverride>(entity => { entity.HasKey(x => x.Id); entity.Property(x => x.Key).HasMaxLength(120).IsRequired(); entity.Property(x => x.State).HasConversion<int>(); entity.Property(x => x.Version).HasDefaultValue(1); entity.Property(x => x.IsUnlimited).HasDefaultValue(false); entity.HasIndex(x => new { x.TenantId, x.Key }).IsUnique(); entity.HasQueryFilter(x => !x.IsDeleted && (tenantExecutionContext != null && tenantExecutionContext.IsBypass || EffectiveTenantId != null && x.TenantId == EffectiveTenantId)); });
        modelBuilder.Entity<EntitlementUsage>(entity => { entity.HasKey(x => x.Id); entity.Property(x => x.Key).HasMaxLength(120).IsRequired(); entity.Property(x => x.Version).IsConcurrencyToken(); entity.HasIndex(x => new { x.TenantId, x.Key }).IsUnique(); entity.HasQueryFilter(x => !x.IsDeleted && (tenantExecutionContext != null && tenantExecutionContext.IsBypass || EffectiveTenantId != null && x.TenantId == EffectiveTenantId)); });
        modelBuilder.Entity<EntitlementReservation>(entity => { entity.HasKey(x => new { x.TenantId, x.Key, x.OperationId }); entity.Property(x => x.Key).HasMaxLength(120).IsRequired(); entity.Property(x => x.Status).HasConversion<int>(); entity.HasIndex(x => new { x.TenantId, x.Key, x.Status }); entity.HasQueryFilter(x => !x.IsDeleted && (tenantExecutionContext != null && tenantExecutionContext.IsBypass || EffectiveTenantId != null && x.TenantId == EffectiveTenantId)); });
        modelBuilder.Entity<DashboardLayout>(entity => { entity.HasKey(x => x.Id); entity.Property(x => x.DashboardKey).HasMaxLength(120).IsRequired(); entity.HasIndex(x => new { x.TenantId, x.UserId, x.DashboardKey }).IsUnique(); entity.HasMany(x => x.Placements).WithOne().HasForeignKey(x => x.LayoutId).OnDelete(DeleteBehavior.Cascade); entity.HasQueryFilter(x => !x.IsDeleted && (tenantExecutionContext != null && tenantExecutionContext.IsBypass || EffectiveTenantId != null && x.TenantId == EffectiveTenantId)); });
        modelBuilder.Entity<DashboardWidgetPlacement>(entity => { entity.HasKey(x => x.Id); entity.Property(x => x.WidgetKey).HasMaxLength(120).IsRequired(); entity.Property(x => x.ModuleKey).HasMaxLength(120).IsRequired(); entity.Property(x => x.SettingsJson).HasMaxLength(16000).IsRequired(); entity.HasIndex(x => new { x.LayoutId, x.WidgetKey }).IsUnique(); });
        modelBuilder.Entity<StoredFileMetadata>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.StorageId).HasMaxLength(64).IsRequired();
            entity.Property(x => x.OriginalFileName).HasMaxLength(260).IsRequired();
            entity.Property(x => x.ContentType).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Purpose).HasMaxLength(120).IsRequired();
            entity.Property(x => x.ReadPermissionCode).HasMaxLength(120).IsRequired();
            entity.Property(x => x.ManagePermissionCode).HasMaxLength(120).IsRequired();
            entity.Property(x => x.DeletionStatus).HasConversion<int>();
            entity.Property(x => x.LastDeletionError).HasMaxLength(2000);
            entity.HasIndex(x => x.StorageId).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.OwnerUserId });
            entity.HasIndex(x => new { x.DeletionStatus, x.NextDeletionAttemptAtUtc });
            entity.HasQueryFilter(x => !x.IsDeleted && (tenantExecutionContext != null && tenantExecutionContext.IsBypass ||
                EffectiveTenantId != null && x.TenantId == EffectiveTenantId));
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.NormalizedName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(320);
            entity.Property(x => x.Phone).HasMaxLength(20);
            entity.Property(x => x.TaxIdentifier).HasMaxLength(14);
            entity.Ignore(x => x.IsActive);
            entity.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.TenantId, x.NormalizedName });
            entity.HasIndex(x => new { x.TenantId, x.Id }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.Phone });
            entity.HasIndex(x => new { x.TenantId, x.Email });
            entity.HasIndex(x => new { x.TenantId, x.TaxIdentifier })
                .IsUnique()
                .HasFilter("\"TaxIdentifier\" IS NOT NULL AND NOT \"IsDeleted\"");
            entity.HasQueryFilter(x => !x.IsDeleted &&
                (tenantExecutionContext != null && tenantExecutionContext.IsBypass ||
                 EffectiveTenantId != null && x.TenantId == EffectiveTenantId));
        });
    }
}
