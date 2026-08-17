using Shine.Domain;
using System.Text.Json;

namespace Billing.Domain;

public static class BillingSensitiveDataGuard
{
    private static readonly string[] ForbiddenFragments =
        ["cardnumber", "paymentcard", "cvv", "cvc", "secret", "accesstoken", "cardtoken", "paymenttoken", "credential"];

    public static void EnsureSafeNames(IEnumerable<string> names)
    {
        if (names.Any(IsForbidden))
            throw new DomainException("Billing data cannot contain payment credentials.");
    }

    public static void EnsureSafeEntries(IReadOnlyDictionary<string, string> entries)
    {
        EnsureSafeNames(entries.Keys);
        EnsureSafeValues(entries.Values);
    }

    public static void EnsureSafeValues(IEnumerable<string> values)
    {
        if (values.Any(IsSensitiveValue))
            throw new DomainException("Billing data cannot contain payment credentials.");
    }

    private static bool IsForbidden(string name)
    {
        var normalized = new string((name ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return normalized is "pan" or "token" || ForbiddenFragments.Any(normalized.Contains);
    }

    private static bool IsSensitiveValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = value.Trim().ToLowerInvariant();
        if (ContainsSensitiveJson(normalized)) return true;
        var credentialMarkers = new[]
        {
            "\"pan\"", "\"cardnumber\"", "\"cvv\"", "\"cvc\"", "\"cardtoken\"",
            "pan=", "pan:", "cardnumber=", "cardnumber:", "cvv=", "cvv:", "cvc=", "cvc:",
            "accesstoken=", "access_token=", "paymenttoken=", "cardtoken="
        };
        if (credentialMarkers.Any(normalized.Contains) ||
            normalized.StartsWith("bearer ", StringComparison.Ordinal) ||
            normalized.StartsWith("sk_live_", StringComparison.Ordinal) ||
            normalized.StartsWith("sk_test_", StringComparison.Ordinal) ||
            normalized.StartsWith("rk_live_", StringComparison.Ordinal) ||
            normalized.StartsWith("tok_", StringComparison.Ordinal) ||
            normalized.StartsWith("pm_", StringComparison.Ordinal) ||
            normalized.StartsWith("card_", StringComparison.Ordinal))
            return true;

        var candidate = new List<char>(19);
        foreach (var character in value.Append('\0'))
        {
            if (char.IsDigit(character))
            {
                candidate.Add(character);
                continue;
            }
            if ((character is ' ' or '-') && candidate.Count > 0) continue;
            if (candidate.Count is >= 13 and <= 19 && PassesLuhn(candidate)) return true;
            candidate.Clear();
        }
        return false;
    }

    private static bool ContainsSensitiveJson(string value)
    {
        if (value.Length == 0 || value[0] is not ('{' or '[')) return false;
        try
        {
            using var document = JsonDocument.Parse(value);
            return ContainsSensitiveJson(document.RootElement);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool ContainsSensitiveJson(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().Any(property =>
            IsForbidden(property.Name) || ContainsSensitiveJson(property.Value)),
        JsonValueKind.Array => element.EnumerateArray().Any(ContainsSensitiveJson),
        JsonValueKind.String => IsSensitiveValue(element.GetString()),
        JsonValueKind.Number => IsSensitiveValue(element.GetRawText()),
        _ => false
    };

    private static bool PassesLuhn(IReadOnlyList<char> digits)
    {
        var sum = 0;
        var doubleDigit = false;
        for (var index = digits.Count - 1; index >= 0; index--)
        {
            var digit = digits[index] - '0';
            if (doubleDigit && (digit *= 2) > 9) digit -= 9;
            sum += digit;
            doubleDigit = !doubleDigit;
        }
        return sum % 10 == 0;
    }
}
