using System.Globalization;
using System.Text.RegularExpressions;
using BudgetinCheck.Api.Features.IncomeMonth;
using BudgetinCheck.Api.Infrastructure.Data;
using Dapper;

namespace BudgetinCheck.Api.Features.ExpenseSummary;

internal sealed partial class ExpenseSummaryService(
    BudgetDbConnectionFactory connectionFactory,
    IncomeMonthService incomeMonthService)
{
    private static readonly CultureInfo EnGbCulture = CultureInfo.GetCultureInfo("en-GB");
    private static readonly Regex MultiSpaceRegex = BuildMultiSpaceRegex();
    private static readonly Regex ExpenseDebtSuffixRegex = BuildExpenseDebtSuffixRegex();
    private static readonly HashSet<string> NonDebtCategoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "food and dining",
        "food & dining",
        "food",
        "dining",
        "transport",
        "travel",
        "transport / travel",
        "transport/travel",
        "savings",
        "saving",
        "emergency",
        "emergency fund",
        "emergency funds",
        "investment",
        "investments",
        "money",
        "allowance",
    };

    public async Task<ExpenseSummaryResponse> GetAsync(
        string budgetPlanId,
        int month,
        int year,
        string scope,
        bool includeBudgetOverview,
        CancellationToken cancellationToken)
    {
        if (month is < 1 or > 12) throw new ArgumentException("Invalid month.");
        if (year < 1900) throw new ArgumentException("Invalid year.");
        if (!string.Equals(scope, "month", StringComparison.Ordinal) && !string.Equals(scope, "pay_period", StringComparison.Ordinal))
        {
            throw new ArgumentException("Invalid scope.");
        }

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        var context = await LoadContextAsync(connection, budgetPlanId, cancellationToken)
            ?? throw new KeyNotFoundException("Budget plan not found");

        var payDate = NormalizePayDate(context.PayDate);
        var payFrequency = NormalizePayFrequency(context.PayFrequency);
        var payAnchorDate = ParsePayAnchorDate(context.PayAnchorDate);

        PayPeriodWindow? payPeriodWindow = null;
        string? periodKey = null;
        string? periodLabel = null;
        string? periodRangeLabel = null;
        int? periodIndex = null;
        var sourceWindowPairs = new List<(int Year, int Month)> { (year, month) };

        if (scope == "pay_period")
        {
            payPeriodWindow = BuildPayPeriodFromMonthAnchor(year, month, payDate, payFrequency, payAnchorDate);
            periodKey = ToIsoDate(payPeriodWindow.Start);
            periodIndex = Math.Max(1, Math.Min(12, month) - 1);
            periodLabel = $"Pay period {periodIndex}";
            periodRangeLabel = FormatPayPeriodLabel(payPeriodWindow.Start, payPeriodWindow.End);
            sourceWindowPairs = BuildSourceWindowPairs(payPeriodWindow.Start, payPeriodWindow.End);
        }

        var categories = await LoadCategoriesAsync(connection, budgetPlanId, cancellationToken);
        var expenseRows = await LoadExpenseRowsAsync(connection, budgetPlanId, scope, sourceWindowPairs, month, year, cancellationToken);

        if (payPeriodWindow is not null)
        {
            expenseRows = FilterPayPeriodExpenses(expenseRows, payPeriodWindow, payDate, payFrequency, year, month);
        }

        var paidMap = await LoadPaidMapAsync(
            connection,
            expenseRows.Select(row => new ExpenseAmountPair { ExpenseId = row.Id, Amount = row.Amount }).ToArray(),
            cancellationToken);

        var categoryMap = categories.ToDictionary(
            category => category.Id,
            category => new ExpenseSummaryCategoryBreakdownResponse
            {
                CategoryId = category.Id,
                Name = category.Name,
                Color = category.Color,
                Icon = category.Icon,
                Total = 0,
                PaidTotal = 0,
                PaidCount = 0,
                TotalCount = 0,
            },
            StringComparer.Ordinal);

        decimal totalAmount = 0;
        decimal paidAmount = 0;
        decimal unpaidAmount = 0;
        var paidCount = 0;
        var unpaidCount = 0;

        foreach (var expense in expenseRows.Where(IncludeInMainExpenseSummary))
        {
            var amount = ClampMoney(expense.Amount);
            paidMap.TryGetValue(expense.Id, out var paidInfo);

            var paid = amount > 0 ? Math.Min(paidInfo?.PaidAmount ?? 0, amount) : 0;
            var isPaid = paidInfo?.IsPaid ?? false;
            var unpaid = Math.Max(0, amount - paid);

            totalAmount += amount;
            paidAmount += paid;
            unpaidAmount += unpaid;

            if (isPaid) paidCount += 1;
            else unpaidCount += 1;

            var categoryId = string.IsNullOrWhiteSpace(expense.CategoryId) ? "__none__" : expense.CategoryId!;
            if (!categoryMap.TryGetValue(categoryId, out var category))
            {
                category = new ExpenseSummaryCategoryBreakdownResponse
                {
                    CategoryId = categoryId,
                    Name = string.IsNullOrWhiteSpace(expense.CategoryName) ? "Uncategorised" : expense.CategoryName!,
                    Color = expense.CategoryColor,
                    Icon = expense.CategoryIcon,
                    Total = 0,
                    PaidTotal = 0,
                    PaidCount = 0,
                    TotalCount = 0,
                };
            }

            categoryMap[categoryId] = new ExpenseSummaryCategoryBreakdownResponse
            {
                CategoryId = category.CategoryId,
                Name = category.Name,
                Color = category.Color,
                Icon = category.Icon,
                Total = RoundMoney(category.Total + amount),
                PaidTotal = RoundMoney(category.PaidTotal + paid),
                PaidCount = category.PaidCount + (isPaid ? 1 : 0),
                TotalCount = category.TotalCount + 1,
            };
        }

        var categoryBreakdown = categoryMap.Values
            .Where(category => category.TotalCount > 0)
            .OrderByDescending(category => category.Total)
            .ToArray();

        var roundedTotalAmount = RoundMoney(totalAmount);
        var roundedPaidAmount = RoundMoney(paidAmount);
        var roundedUnpaidAmount = RoundMoney(unpaidAmount);

        ExpenseSummaryBudgetOverviewResponse? budgetOverview = null;
        if (includeBudgetOverview && payPeriodWindow is not null && periodKey is not null)
        {
            budgetOverview = await BuildBudgetOverviewAsync(
                budgetPlanId,
                month,
                year,
                roundedTotalAmount,
                cancellationToken);
        }

        return new ExpenseSummaryResponse
        {
            Scope = scope,
            Month = month,
            Year = year,
            PeriodLabel = periodLabel,
            PeriodIndex = periodIndex,
            PeriodStart = payPeriodWindow is null ? null : ToIsoDate(payPeriodWindow.Start),
            PeriodEnd = payPeriodWindow is null ? null : ToIsoDate(payPeriodWindow.End),
            PeriodRangeLabel = periodRangeLabel,
            PayDate = payDate,
            PayFrequency = payFrequency,
            TotalCount = expenseRows.Count(IncludeInMainExpenseSummary),
            TotalAmount = roundedTotalAmount,
            PaidCount = paidCount,
            PaidAmount = roundedPaidAmount,
            UnpaidCount = unpaidCount,
            UnpaidAmount = roundedUnpaidAmount,
            CategoryBreakdown = categoryBreakdown,
            BudgetOverview = budgetOverview,
        };
    }

    private static async Task<ExpenseSummaryContextRow?> LoadContextAsync(
        System.Data.Common.DbConnection connection,
        string budgetPlanId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                bp."id" AS "BudgetPlanId",
                bp."payDate" AS "PayDate",
                bp."createdAt" AS "PlanCreatedAt",
                bp."monthlyAllowance" AS "MonthlyAllowance",
                bp."monthlySavingsContribution" AS "MonthlySavingsContribution",
                bp."monthlyEmergencyContribution" AS "MonthlyEmergencyContribution",
                bp."monthlyInvestmentContribution" AS "MonthlyInvestmentContribution",
                uop."payFrequency" AS "PayFrequency",
                uop."payAnchorDate" AS "PayAnchorDate",
                uop."completedAt" AS "CompletedAt",
                uop."updatedAt" AS "ProfileUpdatedAt",
                uop."status" AS "ProfileStatus"
            FROM "BudgetPlan" bp
            LEFT JOIN "UserOnboardingProfile" uop ON uop."userId" = bp."userId"
            WHERE bp."id" = @BudgetPlanId
            LIMIT 1;
            """;

        return await connection.QuerySingleOrDefaultAsync<ExpenseSummaryContextRow>(
            new CommandDefinition(sql, new { BudgetPlanId = budgetPlanId }, cancellationToken: cancellationToken));
    }

    private static async Task<IReadOnlyList<CategoryRow>> LoadCategoriesAsync(
        System.Data.Common.DbConnection connection,
        string budgetPlanId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                c."id" AS "Id",
                c."name" AS "Name",
                c."color" AS "Color",
                c."icon" AS "Icon"
            FROM "Category" c
            WHERE c."budgetPlanId" = @BudgetPlanId
            ORDER BY c."name" ASC;
            """;

        var rows = await connection.QueryAsync<CategoryRow>(
            new CommandDefinition(sql, new { BudgetPlanId = budgetPlanId }, cancellationToken: cancellationToken));

        return rows.ToArray();
    }

    private static async Task<List<ExpenseRow>> LoadExpenseRowsAsync(
        System.Data.Common.DbConnection connection,
        string budgetPlanId,
        string scope,
        IReadOnlyList<(int Year, int Month)> sourceWindowPairs,
        int month,
        int year,
        CancellationToken cancellationToken)
    {
        var sql = scope == "month"
            ? """
                SELECT
                    e."id" AS "Id",
                    e."name" AS "Name",
                    e."amount" AS "Amount",
                    e."seriesKey" AS "SeriesKey",
                    e."periodKey" AS "PeriodKey",
                    e."dueDate" AS "DueDate",
                    e."year" AS "Year",
                    e."month" AS "Month",
                    e."categoryId" AS "CategoryId",
                    c."name" AS "CategoryName",
                    c."color" AS "CategoryColor",
                    c."icon" AS "CategoryIcon",
                    e."isAllocation" AS "IsAllocation",
                    e."isMovedToDebt" AS "IsMovedToDebt",
                    e."isExtraLoggedExpense" AS "IsExtraLoggedExpense",
                    e."paymentSource" AS "PaymentSource"
                FROM "Expense" e
                LEFT JOIN "Category" c ON c."id" = e."categoryId"
                WHERE e."budgetPlanId" = @BudgetPlanId
                  AND e."month" = @Month
                  AND e."year" = @Year
                  AND COALESCE(e."isAllocation", false) = false
                  AND COALESCE(e."isMovedToDebt", false) = false
                ORDER BY e."year" ASC, e."month" ASC, e."createdAt" ASC;
                """
            : BuildPayPeriodExpenseSql(sourceWindowPairs);

        var parameters = new DynamicParameters(new { BudgetPlanId = budgetPlanId, Month = month, Year = year });
        for (var index = 0; index < sourceWindowPairs.Count; index += 1)
        {
            parameters.Add($"Year{index}", sourceWindowPairs[index].Year);
            parameters.Add($"Month{index}", sourceWindowPairs[index].Month);
        }

        var rows = await connection.QueryAsync<ExpenseRow>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        return rows.ToList();
    }

    private static string BuildPayPeriodExpenseSql(IReadOnlyList<(int Year, int Month)> sourceWindowPairs)
    {
        var pairFilters = sourceWindowPairs.Count == 0
            ? "FALSE"
            : string.Join(" OR ", Enumerable.Range(0, sourceWindowPairs.Count)
                .Select(index => $"(e.\"year\" = @Year{index} AND e.\"month\" = @Month{index})"));

        return $$"""
            SELECT
                e."id" AS "Id",
                e."name" AS "Name",
                e."amount" AS "Amount",
                e."seriesKey" AS "SeriesKey",
                e."periodKey" AS "PeriodKey",
                e."dueDate" AS "DueDate",
                e."year" AS "Year",
                e."month" AS "Month",
                e."categoryId" AS "CategoryId",
                c."name" AS "CategoryName",
                c."color" AS "CategoryColor",
                c."icon" AS "CategoryIcon",
                e."isAllocation" AS "IsAllocation",
                e."isMovedToDebt" AS "IsMovedToDebt",
                e."isExtraLoggedExpense" AS "IsExtraLoggedExpense",
                e."paymentSource" AS "PaymentSource"
            FROM "Expense" e
            LEFT JOIN "Category" c ON c."id" = e."categoryId"
            WHERE e."budgetPlanId" = @BudgetPlanId
              AND ({{pairFilters}})
              AND COALESCE(e."isAllocation", false) = false
              AND COALESCE(e."isMovedToDebt", false) = false
            ORDER BY e."year" ASC, e."month" ASC, e."createdAt" ASC;
            """;
    }

    private static async Task<Dictionary<string, PaidInfo>> LoadPaidMapAsync(
        System.Data.Common.DbConnection connection,
        IReadOnlyList<ExpenseAmountPair> expenses,
        CancellationToken cancellationToken)
    {
        var result = expenses.ToDictionary(
            expense => expense.ExpenseId,
            _ => new PaidInfo { PaidAmount = 0, IsPaid = false },
            StringComparer.Ordinal);

        if (expenses.Count == 0) return result;

        const string sql = """
            SELECT
                ep."expenseId" AS "ExpenseId",
                COALESCE(SUM(ep."amount"), 0) AS "PaidAmount"
            FROM "ExpensePayment" ep
            WHERE ep."expenseId" = ANY(@ExpenseIds)
            GROUP BY ep."expenseId";
            """;

        var rows = await connection.QueryAsync<ExpensePaymentAggregateRow>(
            new CommandDefinition(sql, new { ExpenseIds = expenses.Select(expense => expense.ExpenseId).ToArray() }, cancellationToken: cancellationToken));

        var amountMap = expenses.ToDictionary(expense => expense.ExpenseId, expense => expense.Amount, StringComparer.Ordinal);

        foreach (var row in rows)
        {
            if (!result.TryGetValue(row.ExpenseId, out var info)) continue;
            var paid = ClampMoney(row.PaidAmount);
            result[row.ExpenseId] = new PaidInfo
            {
                PaidAmount = paid,
                IsPaid = amountMap.TryGetValue(row.ExpenseId, out var amount) && amount > 0 && paid >= amount,
            };
        }

        return result;
    }

    private static List<ExpenseRow> FilterPayPeriodExpenses(
        IEnumerable<ExpenseRow> rows,
        PayPeriodWindow selectedWindow,
        int payDate,
        string payFrequency,
        int anchorYear,
        int anchorMonth)
    {
        var selectedPeriodKey = ToIsoDate(selectedWindow.Start);
        var allowedUnscheduledYm = new HashSet<string>(StringComparer.Ordinal)
        {
            $"{selectedWindow.Start.Year}-{selectedWindow.Start.Month}",
            $"{selectedWindow.End.Year}-{selectedWindow.End.Month}",
        };

        var seen = new Dictionary<string, RankedExpense>(StringComparer.Ordinal);

        foreach (var expense in rows)
        {
            if (expense.IsAllocation || expense.IsMovedToDebt || !IncludeInMainExpenseSummary(expense)) continue;

            var series = NormalizeSeriesOrName(expense.SeriesKey, expense.Name);
            var amount = ClampMoney(expense.Amount);

            if (expense.DueDate is not null)
            {
                var dueIso = ResolveEffectiveDueDateIso(expense.Year, expense.Month, expense.DueDate, payDate);
                if (dueIso is null) continue;

                var due = ParseIsoDate(dueIso);
                if (due is null || due < selectedWindow.Start || due > selectedWindow.End) continue;

                var rank = (expense.Year == due.Value.Year && expense.Month == due.Value.Month) ? 0 : 1;
                var key = $"{series}|{dueIso}|{amount.ToString(CultureInfo.InvariantCulture)}";
                TryStoreBestExpense(seen, key, expense, rank);
                continue;
            }

            string dedupeScope;
            if (!string.IsNullOrWhiteSpace(expense.PeriodKey))
            {
                var matchedPeriodKey = ResolveMatchedExpensePeriodKey(expense.PeriodKey, selectedWindow.Start, anchorYear, anchorMonth, payFrequency);
                if (matchedPeriodKey is null) continue;
                dedupeScope = $"unscheduled:{matchedPeriodKey}";
            }
            else
            {
                var monthBucket = $"{expense.Year}-{expense.Month}";
                if (!allowedUnscheduledYm.Contains(monthBucket)) continue;
                dedupeScope = $"unscheduled:{monthBucket}";
            }

            var unscheduledKey = $"{series}|{dedupeScope}|{amount.ToString(CultureInfo.InvariantCulture)}";
            TryStoreBestExpense(seen, unscheduledKey, expense, 0);
        }

        return seen.Values.Select(entry => entry.Expense).ToList();
    }

    private static void TryStoreBestExpense(Dictionary<string, RankedExpense> seen, string key, ExpenseRow expense, int rank)
    {
        if (!seen.TryGetValue(key, out var existing) || rank < existing.Rank)
        {
            seen[key] = new RankedExpense { Expense = expense, Rank = rank };
        }
    }

    private static bool IncludeInMainExpenseSummary(ExpenseRow expense)
    {
        if (!expense.IsExtraLoggedExpense) return true;
        return string.Equals((expense.PaymentSource ?? "income").Trim(), "income", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<ExpenseSummaryBudgetOverviewResponse> BuildBudgetOverviewAsync(
        string budgetPlanId,
        int month,
        int year,
        decimal totalAmount,
        CancellationToken cancellationToken)
    {
        var analysis = await incomeMonthService.GetAsync(
            budgetPlanId,
            month,
            year,
            "home_core",
            cancellationToken);

        var totalIncome = RoundMoney(analysis.GrossIncome);
        var plannedDebtPayments = RoundMoney(analysis.PlannedDebtPayments);
        var plannedSetAside = RoundMoney(analysis.IncomeSacrifice);
        var amountLeftToBudget = RoundMoney(totalIncome - plannedDebtPayments - plannedSetAside);
        var totalBudget = RoundMoney(Math.Max(amountLeftToBudget, totalIncome));
        var amountAfterExpenses = RoundMoney(amountLeftToBudget - totalAmount);

        return new ExpenseSummaryBudgetOverviewResponse
        {
            TotalIncome = totalIncome,
            PlannedDebtPayments = plannedDebtPayments,
            IncomeSacrifice = plannedSetAside,
            AmountLeftToBudget = amountLeftToBudget,
            TotalBudget = totalBudget,
            AmountAfterExpenses = amountAfterExpenses,
            IsOverBudgetBySpending = amountAfterExpenses < 0,
        };
    }

    private static async Task<AllocationOverrideRow?> LoadAllocationOverrideAsync(
        System.Data.Common.DbConnection connection,
        string budgetPlanId,
        int month,
        int year,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                ma."monthlyAllowance" AS "MonthlyAllowance",
                ma."monthlySavingsContribution" AS "MonthlySavingsContribution",
                ma."monthlyEmergencyContribution" AS "MonthlyEmergencyContribution",
                ma."monthlyInvestmentContribution" AS "MonthlyInvestmentContribution"
            FROM "MonthlyAllocation" ma
            WHERE ma."budgetPlanId" = @BudgetPlanId
              AND ma."month" = @Month
              AND ma."year" = @Year
            LIMIT 1;
            """;

        return await connection.QuerySingleOrDefaultAsync<AllocationOverrideRow>(
            new CommandDefinition(sql, new { BudgetPlanId = budgetPlanId, Month = month, Year = year }, cancellationToken: cancellationToken));
    }

    private static async Task<decimal> LoadCustomAllocationTotalAsync(
        System.Data.Common.DbConnection connection,
        string budgetPlanId,
        int month,
        int year,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COALESCE(SUM(mai."amount"), 0)
            FROM "MonthlyAllocationItem" mai
            WHERE mai."budgetPlanId" = @BudgetPlanId
              AND mai."month" = @Month
              AND mai."year" = @Year;
            """;

        var value = await connection.ExecuteScalarAsync<decimal?>(
            new CommandDefinition(sql, new { BudgetPlanId = budgetPlanId, Month = month, Year = year }, cancellationToken: cancellationToken));

        return RoundMoney(value ?? 0);
    }

    private static async Task<decimal> LoadIncomeTotalAsync(
        System.Data.Common.DbConnection connection,
        string budgetPlanId,
        int month,
        int year,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COALESCE(SUM(i."amount"), 0)
            FROM "Income" i
            WHERE i."budgetPlanId" = @BudgetPlanId
              AND i."month" = @Month
              AND i."year" = @Year;
            """;

        var value = await connection.ExecuteScalarAsync<decimal?>(
            new CommandDefinition(sql, new { BudgetPlanId = budgetPlanId, Month = month, Year = year }, cancellationToken: cancellationToken));

        return RoundMoney(value ?? 0);
    }

    private static async Task<IReadOnlyList<DebtRow>> LoadDebtRowsAsync(
        System.Data.Common.DbConnection connection,
        string budgetPlanId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                d."id" AS "Id",
                d."name" AS "Name",
                d."amount" AS "Amount",
                d."currentBalance" AS "CurrentBalance",
                d."initialBalance" AS "InitialBalance",
                d."installmentMonths" AS "InstallmentMonths",
                d."monthlyMinimum" AS "MonthlyMinimum",
                d."sourceType" AS "SourceType",
                d."type" AS "Type",
                d."paid" AS "Paid",
                d."sourceExpenseName" AS "SourceExpenseName",
                d."sourceCategoryName" AS "SourceCategoryName",
                d."dueDate" AS "DueDate",
                d."dueDay" AS "DueDay"
            FROM "Debt" d
            WHERE d."budgetPlanId" = @BudgetPlanId
              AND d."paid" = false
              AND d."currentBalance" > 0;
            """;

        var rows = await connection.QueryAsync<DebtRow>(
            new CommandDefinition(sql, new { BudgetPlanId = budgetPlanId }, cancellationToken: cancellationToken));

        return rows.ToArray();
    }

    private static async Task<Dictionary<string, decimal>> LoadDebtOverridesAsync(
        System.Data.Common.DbConnection connection,
        string[] debtIds,
        string periodKey,
        CancellationToken cancellationToken)
    {
        if (debtIds.Length == 0)
        {
            return new Dictionary<string, decimal>(StringComparer.Ordinal);
        }

        const string sql = """
            SELECT
                dppo."debtId" AS "DebtId",
                dppo."amount" AS "Amount"
            FROM "DebtPlannedPaymentOverride" dppo
            WHERE dppo."periodKey" = @PeriodKey
              AND dppo."debtId" = ANY(@DebtIds);
            """;

        var rows = await connection.QueryAsync<DebtOverrideRow>(
            new CommandDefinition(sql, new { DebtIds = debtIds, PeriodKey = periodKey }, cancellationToken: cancellationToken));

        return rows.ToDictionary(row => row.DebtId, row => ClampMoney(row.Amount), StringComparer.Ordinal);
    }

    private static decimal ComputePlannedDebtPayments(
        IReadOnlyList<DebtRow> debts,
        IReadOnlyDictionary<string, decimal> overrides,
        int year,
        int month,
        DateTime periodStart,
        DateTime periodEnd,
        int payDate,
        string payFrequency,
        DateTime? payAnchorDate)
    {
        var regularDebts = debts.Where(debt => !string.Equals(debt.SourceType, "expense", StringComparison.OrdinalIgnoreCase)).ToArray();
        decimal total = 0;

        foreach (var debt in debts)
        {
            if (!ShouldIncludeDebtInPlannedPeriod(debt, regularDebts, year, month, periodStart, periodEnd, payDate, payFrequency, payAnchorDate))
            {
                continue;
            }

            var currentBalance = Math.Max(0, ClampMoney(debt.CurrentBalance));
            var plannedAmount = overrides.TryGetValue(debt.Id, out var overrideAmount)
                ? Math.Min(currentBalance, Math.Max(0, overrideAmount))
                : ComputeMonthlyPlannedPayment(debt);

            total += plannedAmount;
        }

        return total;
    }

    private static bool ShouldIncludeDebtInPlannedPeriod(
        DebtRow debt,
        IReadOnlyList<DebtRow> regularDebts,
        int year,
        int month,
        DateTime periodStart,
        DateTime periodEnd,
        int payDate,
        string payFrequency,
        DateTime? payAnchorDate)
    {
        if (!string.Equals(debt.SourceType, "expense", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IsNonDebtCategoryName(debt.SourceCategoryName)) return false;
        if (IsExpenseDebtCoveredByRegularDebt(debt.SourceExpenseName, debt.SourceCategoryName, regularDebts)) return false;

        var dueDate = ResolveDebtDueDateUtc(debt, year, month, payDate, payFrequency, payAnchorDate);
        if (dueDate is null) return false;

        return dueDate.Value >= periodStart && dueDate.Value <= periodEnd;
    }

    private static decimal ComputeMonthlyPlannedPayment(DebtRow debt)
    {
        var currentBalance = Math.Max(0, ClampMoney(debt.CurrentBalance));
        if (currentBalance <= 0) return 0;

        var amount = Math.Max(0, ClampMoney(debt.Amount));
        var monthlyMinimum = Math.Max(0, ClampMoney(debt.MonthlyMinimum));
        var installmentMonths = debt.InstallmentMonths.GetValueOrDefault();
        var safeInstallmentMonths = installmentMonths > 0 ? installmentMonths : 0;
        var initialBalance = Math.Max(0, ClampMoney(debt.InitialBalance));
        var principal = initialBalance > 0 ? initialBalance : currentBalance;

        decimal planned = 0;
        if (amount > 0)
        {
            planned = amount;
        }
        else if (safeInstallmentMonths > 0 && principal > 0)
        {
            planned = principal / safeInstallmentMonths;
        }

        var isCardType = string.Equals(debt.Type, "credit_card", StringComparison.OrdinalIgnoreCase)
            || string.Equals(debt.Type, "store_card", StringComparison.OrdinalIgnoreCase);

        if (isCardType && monthlyMinimum > 0)
        {
            planned = monthlyMinimum;
        }
        else if (monthlyMinimum > 0)
        {
            planned = Math.Max(planned, monthlyMinimum);
        }

        if (planned <= 0 && string.Equals(debt.SourceType, "expense", StringComparison.OrdinalIgnoreCase))
        {
            planned = amount > 0 ? amount : currentBalance;
        }

        planned = Math.Max(0, planned);
        return Math.Min(currentBalance, RoundMoney(planned));
    }

    private static DateTime? ResolveDebtDueDateUtc(
        DebtRow debt,
        int year,
        int month,
        int payDate,
        string payFrequency,
        DateTime? payAnchorDate)
    {
        if (debt.DueDate is not null)
        {
            return StartOfUtcDay(debt.DueDate.Value);
        }

        if (debt.DueDay is int dueDay and >= 1)
        {
            return ClampDayUtc(year, month - 1, dueDay);
        }

        if (payFrequency != "monthly")
        {
            return BuildPayPeriodFromMonthAnchor(year, month, payDate, payFrequency, payAnchorDate).Start;
        }

        return ClampDayUtc(year, month - 1, payDate);
    }

    private static bool IsNonDebtCategoryName(string? name)
    {
        var normalized = (name ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized)) return false;
        if (NonDebtCategoryNames.Contains(normalized)) return true;
        if (normalized.Contains("food") && normalized.Contains("dining")) return true;
        if (normalized.Contains("transport") || normalized.Contains("travel")) return true;
        if (normalized.Contains("saving") || normalized.Contains("investment")) return true;
        if (normalized.Contains("emergency") && normalized.Contains("fund")) return true;
        return false;
    }

    private static bool IsExpenseDebtCoveredByRegularDebt(string? expenseName, string? sourceCategoryName, IReadOnlyList<DebtRow> regularDebts)
    {
        var tokens = GetExpenseMatchTokens(expenseName, sourceCategoryName);
        if (tokens.Count == 0) return false;

        foreach (var debt in regularDebts)
        {
            if (string.Equals(debt.SourceType, "expense", StringComparison.OrdinalIgnoreCase)) continue;
            if (debt.Paid) continue;
            if (ClampMoney(debt.CurrentBalance) <= 0) continue;

            var normalizedDebtName = NormalizeMatchText(debt.Name);
            if (string.IsNullOrEmpty(normalizedDebtName)) continue;
            if (!normalizedDebtName.Contains("arrears", StringComparison.Ordinal)
                && !normalizedDebtName.Contains("overdue", StringComparison.Ordinal)
                && !normalizedDebtName.Contains("missed", StringComparison.Ordinal))
            {
                continue;
            }

            if (tokens.Any(token => normalizedDebtName.Contains(token, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> GetExpenseMatchTokens(string? expenseName, string? sourceCategoryName)
    {
        var cleanedExpenseName = CleanExpenseDebtBaseName(expenseName, sourceCategoryName);
        var sources = new[] { cleanedExpenseName, sourceCategoryName ?? string.Empty }
            .Select(NormalizeMatchText)
            .Where(value => !string.IsNullOrWhiteSpace(value));

        var tokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            tokens.Add(source);
            foreach (var part in source.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (part.Length >= 4)
                {
                    tokens.Add(part);
                }
            }
        }

        return tokens.ToList();
    }

    private static string CleanExpenseDebtBaseName(string? rawName, string? categoryName)
    {
        var raw = (rawName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(raw)) return raw;

        var value = raw;
        var category = (categoryName ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(category) && value.StartsWith($"{category}:", StringComparison.OrdinalIgnoreCase))
        {
            value = value[(category.Length + 1)..].Trim();
        }

        value = ExpenseDebtSuffixRegex.Replace(value, string.Empty).Trim();
        return string.IsNullOrEmpty(value) ? raw : TitleCaseIfAllCaps(value);
    }

    private static string TitleCaseIfAllCaps(string value)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrEmpty(trimmed)) return trimmed;
        if (!trimmed.Any(char.IsLetter)) return trimmed;
        if (!string.Equals(trimmed, trimmed.ToUpperInvariant(), StringComparison.Ordinal)) return trimmed;
        return EnGbCulture.TextInfo.ToTitleCase(trimmed.ToLowerInvariant()).Trim();
    }

    private static string NormalizeMatchText(string? value)
    {
        var normalized = new string((value ?? string.Empty)
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) || char.IsWhiteSpace(character) ? character : ' ')
            .ToArray());
        return MultiSpaceRegex.Replace(normalized, " ").Trim();
    }

    private static string NormalizeSeriesOrName(string? seriesKey, string? name)
    {
        var raw = (seriesKey ?? name ?? string.Empty).Trim().ToLowerInvariant();
        return MultiSpaceRegex.Replace(raw, " ").Trim();
    }

    private static string? ResolveEffectiveDueDateIso(int year, int month, DateTime? dueDate, int payDate)
    {
        if (dueDate is not null)
        {
            return ToIsoDate(StartOfUtcDay(dueDate.Value));
        }

        if (month is < 1 or > 12) return null;
        var effectiveDate = ClampDayUtc(year, month - 1, payDate);
        return ToIsoDate(effectiveDate);
    }

    private static string? ResolveMatchedExpensePeriodKey(
        string? storedPeriodKey,
        DateTime selectedPeriodStart,
        int anchorYear,
        int anchorMonth,
        string payFrequency)
    {
        var normalized = (storedPeriodKey ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(normalized)) return null;

        var canonicalPeriodKey = ToIsoDate(selectedPeriodStart);
        if (string.Equals(normalized, canonicalPeriodKey, StringComparison.Ordinal)) return canonicalPeriodKey;
        if (payFrequency == "monthly") return null;

        var legacyAnchorMonthPeriodKey = ToIsoDate(new DateTime(anchorYear, anchorMonth, 1, 0, 0, 0, DateTimeKind.Utc));
        return string.Equals(normalized, legacyAnchorMonthPeriodKey, StringComparison.Ordinal) ? canonicalPeriodKey : null;
    }

    private static List<(int Year, int Month)> BuildSourceWindowPairs(DateTime start, DateTime end)
    {
        var pairs = new[]
        {
            (start.Year, start.Month),
            (end.Year, end.Month),
            (start.AddMonths(-1).Year, start.AddMonths(-1).Month),
            (end.AddMonths(1).Year, end.AddMonths(1).Month),
        };

        return pairs.Distinct().ToList();
    }

    private static PayPeriodWindow BuildPayPeriodFromMonthAnchor(
        int anchorYear,
        int anchorMonth,
        int payDate,
        string payFrequency,
        DateTime? payAnchorDate)
    {
        if (payFrequency == "monthly")
        {
            var start = ClampDayUtc(anchorYear, anchorMonth - 2, payDate);
            var end = ClampDayUtc(anchorYear, anchorMonth - 1, payDate).AddDays(-1);
            return new PayPeriodWindow { Start = start, End = StartOfUtcDay(end) };
        }

        var step = IntervalDays(payFrequency);
        var startDate = payAnchorDate is not null
            ? ResolveAnchoredMonthStart(anchorYear, anchorMonth, payAnchorDate.Value, step)
            : ClampDayUtc(anchorYear, anchorMonth - 1, payDate);

        return new PayPeriodWindow
        {
            Start = startDate,
            End = StartOfUtcDay(startDate.AddDays(step - 1)),
        };
    }

    private static DateTime ResolveAnchoredMonthStart(int anchorYear, int anchorMonth, DateTime payAnchorDate, int step)
    {
        var targetStart = new DateTime(anchorYear, anchorMonth, 1, 0, 0, 0, DateTimeKind.Utc);
        var targetEnd = new DateTime(anchorYear, anchorMonth, DateTime.DaysInMonth(anchorYear, anchorMonth), 0, 0, 0, DateTimeKind.Utc);
        var anchor = StartOfUtcDay(payAnchorDate);
        var diffDays = (int)Math.Floor((targetStart - anchor).TotalDays);
        var candidate = anchor.AddDays(Math.Floor((double)diffDays / step) * step);

        while (candidate < targetStart)
        {
            candidate = candidate.AddDays(step);
        }

        var candidates = new List<DateTime>();
        var cursor = candidate;
        while (cursor <= targetEnd)
        {
            candidates.Add(cursor);
            cursor = cursor.AddDays(step);
        }

        if (candidates.Count == 0)
        {
            return candidate;
        }

        var referenceDay = anchor.Day;
        return candidates
            .OrderBy(current => Math.Abs(current.Day - referenceDay))
            .ThenBy(current => current)
            .First();
    }

    private static string FormatPayPeriodLabel(DateTime start, DateTime end)
        => $"{start.Day} {start.ToString("MMM", EnGbCulture)} - {end.Day} {end.ToString("MMM", EnGbCulture)}";

    private static int NormalizePayDate(int payDate)
    {
        if (payDate >= 1) return payDate;
        return 27;
    }

    private static string NormalizePayFrequency(string? payFrequency)
        => payFrequency switch
        {
            "weekly" => "weekly",
            "every_2_weeks" => "every_2_weeks",
            "every_4_weeks" => "every_4_weeks",
            _ => "monthly",
        };

    private static DateTime? ParsePayAnchorDate(DateTime? value)
        => value is null ? null : StartOfUtcDay(value.Value);

    private static int IntervalDays(string payFrequency)
        => payFrequency switch
        {
            "weekly" => 7,
            "every_2_weeks" => 14,
            "every_4_weeks" => 28,
            _ => 0,
        };

    private static DateTime ClampDayUtc(int year, int monthIndex, int day)
    {
        var baseDate = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(monthIndex);
        var maxDay = DateTime.DaysInMonth(baseDate.Year, baseDate.Month);
        var clamped = Math.Max(1, Math.Min(maxDay, day));
        return new DateTime(baseDate.Year, baseDate.Month, clamped, 0, 0, 0, DateTimeKind.Utc);
    }

    private static DateTime StartOfUtcDay(DateTime value)
        => DateTime.SpecifyKind(new DateTime(value.Year, value.Month, value.Day, 0, 0, 0), DateTimeKind.Utc);

    private static decimal ClampMoney(decimal? value) => value is null ? 0 : value.Value;

    private static decimal RoundMoney(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static DateTime? ParseIsoDate(string iso)
    {
        if (!DateTime.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return null;
        }

        return StartOfUtcDay(parsed);
    }

    private static string ToIsoDate(DateTime date) => StartOfUtcDay(date).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex BuildMultiSpaceRegex();

    [GeneratedRegex(@"\s*\((\d{4}-\d{2})(?:\s+\d{4}(?:-\d{2})?)?\)\s*$", RegexOptions.Compiled)]
    private static partial Regex BuildExpenseDebtSuffixRegex();

    private sealed class ExpenseSummaryContextRow
    {
        public string BudgetPlanId { get; set; } = string.Empty;
        public int PayDate { get; set; }
        public DateTime? PlanCreatedAt { get; set; }
        public decimal? MonthlyAllowance { get; set; }
        public decimal? MonthlySavingsContribution { get; set; }
        public decimal? MonthlyEmergencyContribution { get; set; }
        public decimal? MonthlyInvestmentContribution { get; set; }
        public string? PayFrequency { get; set; }
        public DateTime? PayAnchorDate { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? ProfileUpdatedAt { get; set; }
        public string? ProfileStatus { get; set; }
    }

    private sealed class CategoryRow
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Color { get; set; }
        public string? Icon { get; set; }
    }

    private sealed class ExpenseRow
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string? SeriesKey { get; set; }
        public string? PeriodKey { get; set; }
        public DateTime? DueDate { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public string? CategoryId { get; set; }
        public string? CategoryName { get; set; }
        public string? CategoryColor { get; set; }
        public string? CategoryIcon { get; set; }
        public bool IsAllocation { get; set; }
        public bool IsMovedToDebt { get; set; }
        public bool IsExtraLoggedExpense { get; set; }
        public string? PaymentSource { get; set; }
    }

    private sealed class ExpenseAmountPair
    {
        public string ExpenseId { get; init; } = string.Empty;
        public decimal Amount { get; init; }
    }

    private sealed class ExpensePaymentAggregateRow
    {
        public string ExpenseId { get; set; } = string.Empty;
        public decimal PaidAmount { get; set; }
    }

    private sealed class PaidInfo
    {
        public decimal PaidAmount { get; init; }
        public bool IsPaid { get; init; }
    }

    private sealed class RankedExpense
    {
        public ExpenseRow Expense { get; init; } = new();
        public int Rank { get; init; }
    }

    private sealed class AllocationOverrideRow
    {
        public decimal? MonthlyAllowance { get; set; }
        public decimal? MonthlySavingsContribution { get; set; }
        public decimal? MonthlyEmergencyContribution { get; set; }
        public decimal? MonthlyInvestmentContribution { get; set; }
    }

    private sealed class DebtRow
    {
        public string Id { get; set; } = string.Empty;
        public string? Name { get; set; }
        public decimal? Amount { get; set; }
        public decimal? CurrentBalance { get; set; }
        public decimal? InitialBalance { get; set; }
        public int? InstallmentMonths { get; set; }
        public decimal? MonthlyMinimum { get; set; }
        public string? SourceType { get; set; }
        public string? Type { get; set; }
        public bool Paid { get; set; }
        public string? SourceExpenseName { get; set; }
        public string? SourceCategoryName { get; set; }
        public DateTime? DueDate { get; set; }
        public int? DueDay { get; set; }
    }

    private sealed class DebtOverrideRow
    {
        public string DebtId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }

    private sealed class PayPeriodWindow
    {
        public DateTime Start { get; init; }
        public DateTime End { get; init; }
    }
}