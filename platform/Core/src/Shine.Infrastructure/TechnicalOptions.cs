using System.ComponentModel.DataAnnotations;

namespace Shine.Infrastructure;

public sealed class ConnectionStringOptions
{
    [Required]
    public string ShineDb { get; init; } = string.Empty;
}
