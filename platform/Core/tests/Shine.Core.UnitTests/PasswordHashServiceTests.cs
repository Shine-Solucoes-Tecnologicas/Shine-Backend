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
    public void Missing_user_verification_stays_within_the_documented_median_timing_tolerance()
    {
        var encoded = service.Hash("Strong.Password1!");
        for (var index = 0; index < 2; index++)
        {
            service.Verify("warmup", encoded);
            service.Verify("warmup", null);
        }

        var knownSamples = new List<double>();
        var missingSamples = new List<double>();
        for (var index = 0; index < 9; index++)
        {
            // Alternate order to reduce systematic CPU-frequency and runner-load bias.
            if (index % 2 == 0)
            {
                knownSamples.Add(Measure(() => service.Verify("wrong", encoded)).TotalMilliseconds);
                missingSamples.Add(Measure(() => service.Verify("wrong", null)).TotalMilliseconds);
            }
            else
            {
                missingSamples.Add(Measure(() => service.Verify("wrong", null)).TotalMilliseconds);
                knownSamples.Add(Measure(() => service.Verify("wrong", encoded)).TotalMilliseconds);
            }
        }

        var knownMedian = Median(knownSamples);
        var missingMedian = Median(missingSamples);
        var relativeDifference = Math.Abs(missingMedian - knownMedian) / knownMedian;

        // See docs/security/password-verification-timing.md. A 40% median tolerance absorbs
        // shared-runner jitter while still rejecting the former no-PBKDF2 fast path.
        Assert.InRange(relativeDifference, 0, 0.40);
    }

    private static TimeSpan Measure(Action action)
    {
        var stopwatch = Stopwatch.StartNew();
        action();
        stopwatch.Stop();
        return stopwatch.Elapsed;
    }

    private static double Median(IEnumerable<double> samples)
    {
        var ordered = samples.Order().ToArray();
        return ordered[ordered.Length / 2];
    }
}
