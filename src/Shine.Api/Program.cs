using Shine.Application;
using Shine.Infrastructure;
using Shine.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Logging.AddJsonConsole();
var connectionString = builder.Configuration.GetSection("ConnectionStrings").Get<ConnectionStringOptions>()?.ShineDb
    ?? throw new InvalidOperationException("Connection string 'ShineDb' was not configured.");
builder.Services.AddInfrastructure(connectionString);
builder.Services.AddHealthChecks()
    .AddDbContextCheck<Shine.Infrastructure.Persistence.ShineDbContext>("postgresql");
builder.Services.AddControllers();
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
app.UseMiddleware<RequestDiagnosticsMiddleware>();
app.UseMiddleware<JwtAuthenticationMiddleware>();

// Configure the HTTP request pipeline.
app.MapControllers();
app.MapHealthChecks("/health");
app.Run();
