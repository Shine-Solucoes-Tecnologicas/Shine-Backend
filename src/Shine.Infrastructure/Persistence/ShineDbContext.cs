using Microsoft.EntityFrameworkCore;

namespace Shine.Infrastructure.Persistence;

public sealed class ShineDbContext(DbContextOptions<ShineDbContext> options) : DbContext(options)
{
}
