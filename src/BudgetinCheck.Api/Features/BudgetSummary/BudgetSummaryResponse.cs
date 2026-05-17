namespace BudgetinCheck.Api.Features.BudgetSummary;

internal sealed record BudgetSummaryResponse(
    string Month,
    int Year,
    decimal IncomeTotal,
    decimal ExpenseTotal,
    decimal DebtPaymentsTotal,
    decimal SpendingTotal,
    decimal PlannedAllowance,
    decimal PlannedSavings,
    decimal PlannedEmergency,
    decimal PlannedInvestments,
    decimal Unallocated);