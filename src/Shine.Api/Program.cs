using Shine.Application;
using Shine.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
var connectionString = builder.Configuration.GetConnectionString("ShineDb")
    ?? throw new InvalidOperationException("Connection string 'ShineDb' was not configured.");
builder.Services.AddInfrastructure(connectionString);
builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapControllers();
app.Run();
