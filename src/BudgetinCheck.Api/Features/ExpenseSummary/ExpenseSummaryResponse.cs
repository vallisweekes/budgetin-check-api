namespace BudgetinCheck.Api.Features.ExpenseSummary;

internal sealed class ExpenseSummaryCategoryBreakdownResponse
{
    public string CategoryId { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Color { get; init; }

    public string? Icon { get; init; }

    public decimal Total { get; init; }

    public decimal PaidTotal { get; init; }

    public int PaidCount { get; init; }

    public int TotalCount { get; init; }
}

internal sealed class ExpenseSummaryBudgetOverviewResponse
{
    public decimal TotalIncome { get; init; }

    public decimal PlannedDebtPayments { get; init; }

    public decimal IncomeSacrifice { get; init; }

    public decimal AmountLeftToBudget { get; init; }

    public decimal TotalBudget { get; init; }

    public decimal AmountAfterExpenses { get; init; }

    public bool IsOverBudgetBySpending { get; init; }
}

internal sealed class ExpenseSummaryResponse
{
    public string Scope { get; init; } = "month";

    public int Month { get; init; }

    public int Year { get; init; }

    public string? PeriodLabel { get; init; }

    public int? PeriodIndex { get; init; }

    public string? PeriodStart { get; init; }

    public string? PeriodEnd { get; init; }

    public string? PeriodRangeLabel { get; init; }

    public int? PayDate { get; init; }

    public string? PayFrequency { get; init; }

    public int TotalCount { get; init; }

    public decimal TotalAmount { get; init; }

    public int PaidCount { get; init; }

    public decimal PaidAmount { get; init; }

    public int UnpaidCount { get; init; }

    public decimal UnpaidAmount { get; init; }

    public IReadOnlyList<ExpenseSummaryCategoryBreakdownResponse> CategoryBreakdown { get; init; } = [];

    public ExpenseSummaryBudgetOverviewResponse? BudgetOverview { get; init; }
}