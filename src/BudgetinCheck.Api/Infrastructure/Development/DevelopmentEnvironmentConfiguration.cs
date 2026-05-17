using System.Text;

namespace BudgetinCheck.Api.Infrastructure.Development;

internal static class DevelopmentEnvironmentConfiguration
{
    public static void ApplyBudgetAppDatabaseFallback(
        ConfigurationManager configuration,
        IWebHostEnvironment environment)
    {
        if (!environment.IsDevelopment())
        {
            return;
        }

        ApplyLegacyNextJsBaseUrlFallback(configuration, environment.ContentRootPath);

        if (!string.IsNullOrWhiteSpace(configuration.GetConnectionString("BudgetDb"))
            || !string.IsNullOrWhiteSpace(configuration["ConnectionStrings:BudgetDb"])
            || !string.IsNullOrWhiteSpace(configuration["DATABASE_URL"]))
        {
            return;
        }

        foreach (var filePath in GetBudgetAppEnvFiles(environment.ContentRootPath))
        {
            if (!File.Exists(filePath))
            {
                continue;
            }

            var values = ParseEnvFile(filePath);
            if (!values.TryGetValue("DATABASE_URL", out var databaseUrl)
                || string.IsNullOrWhiteSpace(databaseUrl))
            {
                continue;
            }

            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DATABASE_URL"] = databaseUrl,
                ["ConnectionStrings:BudgetDb"] = databaseUrl,
            });

            return;
        }
    }

    private static void ApplyLegacyNextJsBaseUrlFallback(ConfigurationManager configuration, string contentRootPath)
    {
        var currentBaseUrl = configuration["LegacyNextJs:BaseUrl"];
        if (!ShouldOverrideLegacyBaseUrl(currentBaseUrl))
        {
            return;
        }

        foreach (var filePath in GetBudgetAppEnvFiles(contentRootPath, includeExample: true))
        {
            if (!File.Exists(filePath))
            {
                continue;
            }

            var values = ParseEnvFile(filePath);
            var baseUrl = GetLegacyBaseUrl(values);
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                continue;
            }

            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LegacyNextJs:BaseUrl"] = baseUrl,
            });

            return;
        }

        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LegacyNextJs:BaseUrl"] = "http://localhost:5537",
        });
    }

    private static bool ShouldOverrideLegacyBaseUrl(string? currentBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(currentBaseUrl))
        {
            return true;
        }

        return string.Equals(currentBaseUrl, "http://localhost:3000", StringComparison.OrdinalIgnoreCase)
            || string.Equals(currentBaseUrl, "https://localhost:3000", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetLegacyBaseUrl(IReadOnlyDictionary<string, string?> values)
    {
        if (values.TryGetValue("APP_URL", out var appUrl) && !string.IsNullOrWhiteSpace(appUrl))
        {
            return appUrl;
        }

        if (values.TryGetValue("NEXTAUTH_URL", out var nextAuthUrl) && !string.IsNullOrWhiteSpace(nextAuthUrl))
        {
            return nextAuthUrl;
        }

        return null;
    }

    private static IEnumerable<string> GetBudgetAppEnvFiles(string contentRootPath, bool includeExample = false)
    {
        var budgetAppWebClientRoot = Path.GetFullPath(
            Path.Combine(contentRootPath, "..", "..", "..", "budget-app", "web-client"));

        yield return Path.Combine(budgetAppWebClientRoot, ".env.local");
        yield return Path.Combine(budgetAppWebClientRoot, ".env");

        if (includeExample)
        {
            yield return Path.Combine(budgetAppWebClientRoot, ".env.example");
        }
    }

    private static Dictionary<string, string?> ParseEnvFile(string filePath)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in File.ReadLines(filePath))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line[7..].TrimStart();
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var value = line[(separatorIndex + 1)..].Trim();
            values[key] = Unquote(value);
        }

        return values;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            var innerValue = value[1..^1];
            return RegexUnescape(innerValue);
        }

        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
        {
            return value[1..^1];
        }

        return value;
    }

    private static string RegexUnescape(string value)
    {
        var builder = new StringBuilder(value.Length);

        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (current == '\\' && index + 1 < value.Length)
            {
                index++;
                builder.Append(value[index] switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    _ => value[index],
                });

                continue;
            }

            builder.Append(current);
        }

        return builder.ToString();
    }
}