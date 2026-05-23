namespace BudgetinCheck.Api.Features.IncomeMonth;

internal sealed class IncomeMonthIncomeItemResponse
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public decimal Amount { get; init; }
}

internal sealed class IncomeMonthExpensePreviewItemResponse
{
    public string ExpenseId { get; init; } = string.Empty;

    public string ExpenseName { get; init; } = string.Empty;

    public string PlanId { get; init; } = string.Empty;

    public string PlanName { get; init; } = string.Empty;

    public decimal Amount { get; init; }
}

internal sealed class IncomeMonthExpensePreviewResponse
{
    public IReadOnlyList<IncomeMonthExpensePreviewItemResponse> Items { get; init; } = Array.Empty<IncomeMonthExpensePreviewItemResponse>();

    public int RemainingCount { get; init; }
}

internal sealed class IncomeMonthExpenseBreakdownResponse
{
    public decimal SelectedPlanExpenses { get; init; }

    public decimal AdditionalPlansExpenses { get; init; }

    public IncomeMonthExpensePreviewResponse SelectedPlanPreview { get; init; } = new();

    public IncomeMonthExpensePreviewResponse AdditionalPlansPreview { get; init; } = new();
}

internal sealed class IncomeMonthSetAsideBreakdownResponse
{
    public decimal Savings { get; init; }

    public decimal Emergency { get; init; }

    public decimal Investments { get; init; }

    public decimal Custom { get; init; }
}

internal sealed class IncomeMonthResponse
{
    public string BudgetPlanId { get; init; } = string.Empty;

    public int Month { get; init; }

    public int Year { get; init; }

    public string MonthKey { get; init; } = string.Empty;

    public string? PeriodLabel { get; init; }

    public string? PeriodStart { get; init; }

    public string? PeriodEnd { get; init; }

    public string? PeriodRangeLabel { get; init; }

    public IReadOnlyList<IncomeMonthIncomeItemResponse> IncomeItems { get; init; } = Array.Empty<IncomeMonthIncomeItemResponse>();

    public decimal GrossIncome { get; init; }

    public int SourceCount { get; init; }

    public decimal PlannedExpenses { get; init; }

    public decimal PaidExpenses { get; init; }

    public IncomeMonthExpenseBreakdownResponse ExpenseBreakdown { get; init; } = new();

    public decimal PlannedDebtPayments { get; init; }

    public decimal PaidDebtPayments { get; init; }

    public decimal MonthlyAllowance { get; init; }

    public decimal IncomeSacrifice { get; init; }

    public IncomeMonthSetAsideBreakdownResponse SetAsideBreakdown { get; init; } = new();

    public decimal PlannedBills { get; init; }

    public decimal PaidBillsSoFar { get; init; }

    public decimal RemainingExpenseBills { get; init; }

    public decimal RemainingDebtBills { get; init; }

    public decimal RemainingBills { get; init; }

    public decimal LeftToPayRightNow { get; init; }

    public decimal MoneyLeftAfterPlan { get; init; }

    public decimal IncomeLeftRightNow { get; init; }

    public decimal SpendableIncomeRightNow { get; init; }

    public decimal MoneyOutTotal { get; init; }

    public bool IsOnPlan { get; init; }

    public decimal IncomeSacrificePct { get; init; }

    public decimal MoneyLeftPctOfGross { get; init; }

    public decimal? MoneyLeftVsLastMonthPct { get; init; }

    public string PlanStatusTag { get; init; } = "on_plan";

    public string PlanStatusDescription { get; init; } = "On plan";
}