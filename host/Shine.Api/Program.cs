using Microsoft.AspNetCore.Authorization;
using Shine.Application;
using Shine.Infrastructure;
using Shine.Api;
using Scheduling.Infrastructure;
using Billing.Infrastructure;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.ValidateProductionConfiguration(builder.Environment);
builder.Services.AddApplication();
builder.Logging.AddJsonConsole();
var connectionString = builder.Configuration.GetSection("ConnectionStrings").Get<ConnectionStringOptions>()?.ShineDb
    ?? throw new InvalidOperationException("Connection string 'ShineDb' was not configured.");
builder.Services.AddInfrastructure(connectionString);
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection("RabbitMq"));
builder.Services.AddScheduling(connectionString);
builder.Services.AddBillingInfrastructure(connectionString);
builder.Services.AddHealthChecks()
    .AddDbContextCheck<Shine.Infrastructure.Persistence.ShineDbContext>("postgresql")
    .AddCheck<RabbitMqHealthCheck>("rabbitmq");
builder.Services.AddControllers();
builder.Services.AddScoped<AppointmentEntitlementReconciliationService>();
builder.Services.AddHostedService<AppointmentEntitlementReconciliationWorker>();
builder.Services.AddHostedService<StoredFileDeletionWorker>();
builder.Services.AddApiSecurityConfiguration(builder.Configuration, builder.Environment);
builder.Services.AddRateLimiter(options => options.AddPolicy("public-scheduling", httpContext =>
    RateLimitPartition.GetFixedWindowLimiter(
        $"{httpContext.Connection.RemoteIpAddress}:{httpContext.Request.RouteValues["tenantId"]}",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
builder.Services.AddHttpContextAccessor();
builder.Services.AddShineJwtAuthentication();
builder.Services.AddSingleton<IPasswordRecoveryMessageTemplate, PasswordRecoveryMessageTemplate>();
if (builder.Configuration.GetValue<bool>("PasswordRecovery:MockDelivery"))
    builder.Services.AddSingleton<IPasswordRecoveryDelivery, InMemoryPasswordRecoveryDelivery>();
else
    builder.Services.AddSingleton<IPasswordRecoveryDelivery, NullPasswordRecoveryDelivery>();
builder.Services.AddAuthorization();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddScoped<IAuthorizationHandler, GlobalPermissionHandler>();
builder.Services.AddScoped<IAuthorizationHandler, ModuleHandler>();
builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState.Values.SelectMany(value => value.Errors)
            .Select(error => new { code = "validation_error", message = error.ErrorMessage ?? "The supplied value is invalid." });
        return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(new { errors });
    };
});

var app = builder.Build();

app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseMiddleware<NullRemoteForwardedHeadersGuardMiddleware>();
app.UseForwardedHeaders();
app.UseRouting();
app.UseCors(SecurityConfiguration.CorsPolicy);
app.UseMiddleware<RequestDiagnosticsMiddleware>();
app.UseMiddleware<AuthenticationRateLimitIdentityMiddleware>();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Configure the HTTP request pipeline.
app.MapControllers();
app.MapHealthChecks("/health");
app.Run();

public partial class Program;
