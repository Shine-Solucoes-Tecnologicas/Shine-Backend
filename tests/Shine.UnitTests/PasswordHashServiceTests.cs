using Shine.Infrastructure;
using System.Diagnostics;

namespace Shine.UnitTests;

public sealed class PasswordHashServiceTests
{
    private readonly Pbkdf2PasswordHashService service = new();

    [Fact]
    public void Hashes_are_unique_and_verify_correct_password()
    {
        var first = service.Hash("Strong.Password1!");
        var second = service.Hash("Strong.Password1!");

        Assert.NotEqual(first, second);
        Assert.True(service.Verify("Strong.Password1!", first));
        Assert.False(service.Verify("Wrong.Password1!", first));
    }

    [Fact]
    public void Missing_user_verification_uses_a_comparable_pbkdf2_cost()
    {
        var encoded = service.Hash("Strong.Password1!");
        service.Verify("warmup", encoded);
        service.Verify("warmup", null);

        var knownDuration = Measure(() => service.Verify("wrong", encoded), 3);
        var missingDuration = Measure(() => service.Verify("wrong", null), 3);
        var ratio = missingDuration.TotalMilliseconds / knownDuration.TotalMilliseconds;

        // Timing varies on shared CI runners; this broad bound catches a fast-path regression
        // while avoiding an unrealistic equality requirement between measurements.
        Assert.InRange(ratio, 0.25, 4.0);
    }

    private static TimeSpan Measure(Action action, int repetitions)
    {
        var stopwatch = Stopwatch.StartNew();
        for (var index = 0; index < repetitions; index++) action();
        stopwatch.Stop();
        return stopwatch.Elapsed;
    }
}
