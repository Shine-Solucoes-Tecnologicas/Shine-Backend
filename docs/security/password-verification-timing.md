# Password verification timing regression

`PasswordHashServiceTests.Missing_user_verification_stays_within_the_documented_median_timing_tolerance`
guards against reintroducing a fast path for unknown users.

The test warms both paths, collects nine interleaved samples for a stored PBKDF2 hash and the
dummy hash, and compares their medians. Alternating the execution order reduces CPU-frequency
and shared-runner load bias. The accepted relative median difference is at most 40%.

This tolerance does not claim that HTTP requests are constant-time. It is deliberately broad
enough for shared CI runners, but a missing PBKDF2 operation is orders of magnitude faster and
will fail the test. Run it with:

```powershell
dotnet test .\platform\Core\tests\Shine.Core.UnitTests\Shine.Core.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~PasswordHashServiceTests"
```
