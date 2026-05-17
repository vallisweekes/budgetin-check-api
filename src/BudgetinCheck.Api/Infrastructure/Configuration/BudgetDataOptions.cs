namespace BudgetinCheck.Api.Infrastructure.Configuration;

internal sealed class BudgetDataOptions
{
    public const string SectionName = "BudgetData";

    public string? SpendingDataRoot { get; set; }
}