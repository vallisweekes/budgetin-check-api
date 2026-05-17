using System.Globalization;

namespace BudgetinCheck.Api.Features.Common;

internal static class MonthKeys
{
    private static readonly string[] CanonicalKeys =
    {
        "JANUARY",
        "FEBURARY",
        "MARCH",
        "APRIL",
        "MAY",
        "JUNE",
        "JULY",
        "AUGUST ",
        "SEPTEMBER",
        "OCTOBER",
        "NOVEMBER",
        "DECEMBER",
    };

    private static readonly Dictionary<string, (int MonthNumber, string MonthKey)> Lookup = BuildLookup();

    public static bool TryResolve(string? raw, out int monthNumber, out string monthKey)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            monthNumber = DateTime.UtcNow.Month;
            monthKey = CanonicalKeys[monthNumber - 1];
            return true;
        }

        var trimmed = raw.Trim();
        if (int.TryParse(trimmed, out var numericMonth) && numericMonth is >= 1 and <= 12)
        {
            monthNumber = numericMonth;
            monthKey = CanonicalKeys[numericMonth - 1];
            return true;
        }

        var normalized = trimmed.ToUpperInvariant();
        if (Lookup.TryGetValue(normalized, out var resolved))
        {
            monthNumber = resolved.MonthNumber;
            monthKey = resolved.MonthKey;
            return true;
        }

        monthNumber = 0;
        monthKey = string.Empty;
        return false;
    }

    private static Dictionary<string, (int MonthNumber, string MonthKey)> BuildLookup()
    {
        var lookup = new Dictionary<string, (int MonthNumber, string MonthKey)>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < CanonicalKeys.Length; index += 1)
        {
            var monthNumber = index + 1;
            var key = CanonicalKeys[index];
            var trimmedKey = key.Trim();
            var shortKey = trimmedKey[..Math.Min(3, trimmedKey.Length)];

            lookup[trimmedKey] = (monthNumber, key);
            lookup[shortKey] = (monthNumber, key);
            lookup[monthNumber.ToString(CultureInfo.InvariantCulture)] = (monthNumber, key);

            if (trimmedKey == "FEBURARY")
            {
                lookup["FEBRUARY"] = (monthNumber, key);
                lookup["FEB"] = (monthNumber, key);
            }

            if (trimmedKey == "AUGUST")
            {
                lookup["AUGUST"] = (monthNumber, key);
                lookup["AUG"] = (monthNumber, key);
            }
        }

        return lookup;
    }
}