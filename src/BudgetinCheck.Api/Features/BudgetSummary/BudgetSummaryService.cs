using System.Data;
using System.Text.Json;
using BudgetinCheck.Api.Features.Common;
using BudgetinCheck.Api.Infrastructure.Configuration;
using BudgetinCheck.Api.Infrastructure.Data;
using Dapper;
using Microsoft.Extensions.Options;

namespace BudgetinCheck.Api.Features.BudgetSummary;

internal sealed class BudgetSummaryService(
    BudgetDbConnectionFactory connectionFactory,
    IOptions<BudgetDataOptions> budgetDataOptions)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<BudgetSummaryResponse> GetAsync(
        string budgetPlanId,
        string? rawMonth,
        int? requestedYear,
        CancellationToken cancellationToken)
    {
        if (!MonthKeys.TryResolve(rawMonth, out var monthNumber, out var monthKey))
        {
            throw new ArgumentException($"Invalid month: '{rawMonth}'.", nameof(rawMonth));
        }

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var year = requestedYear ?? await ResolveSummaryYearAsync(connection, budgetPlanId, cancellationToken);

        var plan = await connection.QuerySingleOrDefaultAsync<PlanAllocationRow>(
            new CommandDefinition(
                @"
SELECT
    ""monthlyAllowance"" AS ""MonthlyAllowance"",
    ""monthlySavingsContribution"" AS ""MonthlySavingsContribution"",
    ""monthlyEmergencyContribution"" AS ""MonthlyEmergencyContribution"",
    ""monthlyInvestmentContribution"" AS ""MonthlyInvestmentContribution""
FROM ""BudgetPlan""
WHERE ""id"" = @BudgetPlanId",
                new { BudgetPlanId = budgetPlanId },
                cancellationToken: cancellationToken));

        if (plan is null)
        {
            throw new KeyNotFoundException($"Budget plan '{budgetPlanId}' was not found.");
        }

        var allocationOverride = await connection.QuerySingleOrDefaultAsync<PlanAllocationRow>(
            new CommandDefinition(
                @"
SELECT
    ""monthlyAllowance"" AS ""MonthlyAllowance"",
    ""monthlySavingsContribution"" AS ""MonthlySavingsContribution"",
    ""monthlyEmergencyContribution"" AS ""MonthlyEmergencyContribution"",
    ""monthlyInvestmentContribution"" AS ""MonthlyInvestmentContribution""
FROM ""MonthlyAllocation""
WHERE ""budgetPlanId"" = @BudgetPlanId
  AND ""year"" = @Year
  AND ""month"" = @Month",
                new { BudgetPlanId = budgetPlanId, Year = year, Month = monthNumber },
                cancellationToken: cancellationToken));

        var incomeTotal = await connection.ExecuteScalarAsync<decimal>(
            new CommandDefinition(
                @"
SELECT COALESCE(SUM(""amount""), 0)
FROM ""Income""
WHERE ""budgetPlanId"" = @BudgetPlanId
  AND ""year"" = @Year
  AND ""month"" = @Month",
                new { BudgetPlanId = budgetPlanId, Year = year, Month = monthNumber },
                cancellationToken: cancellationToken));

        var expenseTotal = await connection.ExecuteScalarAsync<decimal>(
            new CommandDefinition(
                @"
SELECT COALESCE(SUM(""amount""), 0)
FROM ""Expense""
WHERE ""budgetPlanId"" = @BudgetPlanId
  AND ""year"" = @Year
  AND ""month"" = @Month",
                new { BudgetPlanId = budgetPlanId, Year = year, Month = monthNumber },
                cancellationToken: cancellationToken));

        var debtPaymentsTotal = await connection.ExecuteScalarAsync<decimal>(
            new CommandDefinition(
                @"
SELECT COALESCE(SUM(dp.""amount""), 0)
FROM ""DebtPayment"" dp
INNER JOIN ""Debt"" d ON d.""id"" = dp.""debtId""
WHERE d.""budgetPlanId"" = @BudgetPlanId
  AND dp.""year"" = @Year
  AND dp.""month"" = @Month
  AND dp.""source""::text = 'income'",
                new { BudgetPlanId = budgetPlanId, Year = year, Month = monthNumber },
                cancellationToken: cancellationToken));

        var nonIncomeExpensePaymentsTotal = await connection.ExecuteScalarAsync<decimal>(
            new CommandDefinition(
                @"
SELECT COALESCE(SUM(ep.""amount""), 0)
FROM ""ExpensePayment"" ep
INNER JOIN ""Expense"" e ON e.""id"" = ep.""expenseId""
WHERE e.""budgetPlanId"" = @BudgetPlanId
  AND e.""year"" = @Year
  AND e.""month"" = @Month
  AND ep.""source""::text = ANY(@Sources)",
                new
                {
                    BudgetPlanId = budgetPlanId,
                    Year = year,
                    Month = monthNumber,
                    Sources = new[] { "savings", "emergency", "extra_untracked" },
                },
                cancellationToken: cancellationToken));

        var spendingTotal = await ReadSpendingTotalAsync(budgetPlanId, monthNumber, cancellationToken);
        var plannedAllowance = allocationOverride?.MonthlyAllowance ?? plan.MonthlyAllowance;
        var plannedSavings = allocationOverride?.MonthlySavingsContribution ?? plan.MonthlySavingsContribution;
        var plannedEmergency = allocationOverride?.MonthlyEmergencyContribution ?? plan.MonthlyEmergencyContribution;
        var plannedInvestments = allocationOverride?.MonthlyInvestmentContribution ?? plan.MonthlyInvestmentContribution;

        var unallocated =
            incomeTotal -
            expenseTotal -
            debtPaymentsTotal -
            plannedAllowance -
            plannedSavings -
            plannedEmergency -
            plannedInvestments +
            nonIncomeExpensePaymentsTotal;

        return new BudgetSummaryResponse(
            Month: monthKey,
            Year: year,
            IncomeTotal: incomeTotal,
            ExpenseTotal: expenseTotal,
            DebtPaymentsTotal: debtPaymentsTotal,
            SpendingTotal: spendingTotal,
            PlannedAllowance: plannedAllowance,
            PlannedSavings: plannedSavings,
            PlannedEmergency: plannedEmergency,
            PlannedInvestments: plannedInvestments,
            Unallocated: unallocated);
    }

    private async Task<int> ResolveSummaryYearAsync(IDbConnection connection, string budgetPlanId, CancellationToken cancellationToken)
    {
        var plan = await connection.QuerySingleOrDefaultAsync<SummaryYearPlanRow>(
            new CommandDefinition(
                @"
SELECT
    ""kind""::text AS ""Kind"",
    ""eventDate"" AS ""EventDate""
FROM ""BudgetPlan""
WHERE ""id"" = @BudgetPlanId",
                new { BudgetPlanId = budgetPlanId },
                cancellationToken: cancellationToken));

        if (plan?.EventDate is not null && (plan.Kind == "holiday" || plan.Kind == "carnival"))
        {
            return plan.EventDate.Value.Year;
        }

        var latestIncomeYear = await connection.QuerySingleOrDefaultAsync<int?>(
            new CommandDefinition(
                @"
SELECT ""year""
FROM ""Income""
WHERE ""budgetPlanId"" = @BudgetPlanId
ORDER BY ""year"" DESC, ""month"" DESC
LIMIT 1",
                new { BudgetPlanId = budgetPlanId },
                cancellationToken: cancellationToken));

        if (latestIncomeYear is not null)
        {
            return latestIncomeYear.Value;
        }

        var latestExpenseYear = await connection.QuerySingleOrDefaultAsync<int?>(
            new CommandDefinition(
                @"
SELECT ""year""
FROM ""Expense""
WHERE ""budgetPlanId"" = @BudgetPlanId
ORDER BY ""year"" DESC, ""month"" DESC
LIMIT 1",
                new { BudgetPlanId = budgetPlanId },
                cancellationToken: cancellationToken));

        return latestExpenseYear ?? DateTime.UtcNow.Year;
    }

    private async Task<decimal> ReadSpendingTotalAsync(string budgetPlanId, int monthNumber, CancellationToken cancellationToken)
    {
        var root = budgetDataOptions.Value.SpendingDataRoot;
        if (string.IsNullOrWhiteSpace(root))
        {
            return 0m;
        }

        var filePath = Path.Combine(root, $"{budgetPlanId}.json");
        if (!File.Exists(filePath))
        {
            return 0m;
        }

        await using var stream = File.OpenRead(filePath);
        var entries = await JsonSerializer.DeserializeAsync<List<SpendingEntry>>(stream, JsonOptions, cancellationToken)
            ?? new List<SpendingEntry>();

        decimal total = 0m;
        foreach (var entry in entries)
        {
            if (!MonthKeys.TryResolve(entry.Month, out var entryMonthNumber, out _))
            {
                continue;
            }

            if (entryMonthNumber == monthNumber)
            {
                total += entry.Amount;
            }
        }

        return total;
    }

    private sealed class SummaryYearPlanRow
    {
        public string? Kind { get; init; }

        public DateTimeOffset? EventDate { get; init; }
    }

    private sealed class PlanAllocationRow
    {
        public decimal MonthlyAllowance { get; init; }

        public decimal MonthlySavingsContribution { get; init; }

        public decimal MonthlyEmergencyContribution { get; init; }

        public decimal MonthlyInvestmentContribution { get; init; }
    }

    private sealed class SpendingEntry
    {
        public string? Month { get; init; }

        public decimal Amount { get; init; }
    }
}