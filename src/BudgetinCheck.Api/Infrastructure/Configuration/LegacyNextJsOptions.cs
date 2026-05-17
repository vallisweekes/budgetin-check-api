namespace BudgetinCheck.Api.Infrastructure.Configuration;

internal sealed class LegacyNextJsOptions
{
    public const string SectionName = "LegacyNextJs";

    public string BaseUrl { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 100;
}