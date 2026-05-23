using System.Globalization;
using BudgetinCheck.Api.Features.Common;
using BudgetinCheck.Api.Infrastructure.Data;
using Dapper;

namespace BudgetinCheck.Api.Features.IncomeMonth;

internal sealed class IncomeMonthService(BudgetDbConnectionFactory connectionFactory)
{
    private static readonly CultureInfo EnGbCulture = CultureInfo.GetCultureInfo("en-GB");
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

    public async Task<IncomeMonthResponse> GetAsync(
        string budgetPlanId,
        int? requestedMonth,
        int? requestedYear,
        string mode,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(mode, "full", StringComparison.Ordinal) && !string.Equals(mode, "home_core", StringComparison.Ordinal))
        {
            throw new ArgumentException("Invalid mode.");
        }

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        var context = await LoadContextAsync(connection, budgetPlanId, cancellationToken)
            ?? throw new KeyNotFoundException("Budget plan not found");

        var payDate = NormalizePayDate(context.PayDate);
        var payFrequency = NormalizePayFrequency(context.PayFrequency);
        var payAnchorDate = payFrequency == "monthly" ? null : ParsePayAnchorDate(context.PayAnchorDate);
        var month = requestedMonth ?? DateTime.UtcNow.Month;
        var year = requestedYear ?? DateTime.UtcNow.Year;

        if (month is < 1 or > 12) throw new ArgumentException("Invalid month.");
        if (year < 1900) throw new ArgumentException("Invalid year.");

        MonthKeys.TryResolve(month.ToString(CultureInfo.InvariantCulture), out _, out var monthKey);

        var eventScope = ResolveEventScope(context.Kind, context.EventDate);
        var expensePlanScope = await ResolveExpensePlanScopeAsync(connection, context, budgetPlanId, mode, cancellationToken);

        PayPeriodWindow? periodWindow = null;
        string? periodKey = null;
        string? periodLabel = null;
        string? periodStart = null;
        string? periodEnd = null;
        string? periodRangeLabel = null;
        if (payFrequency == "monthly")
        {
            periodWindow = BuildPayPeriodFromMonthAnchor(year, month, payDate, payFrequency, payAnchorDate);
            periodKey = ToIsoDate(periodWindow.Start);
            periodLabel = $"Pay period {Math.Max(1, Math.Min(12, month) - 1)}";
            periodStart = ToIsoDate(periodWindow.Start);
            periodEnd = ToIsoDate(periodWindow.End);
            periodRangeLabel = FormatPayPeriodLabel(periodWindow.Start, periodWindow.End);
        }

        var incomeItems = payFrequency == "monthly"
            ? await LoadIncomeForAnchorMonthAsync(connection, budgetPlanId, year, month, payDate, payFrequency, payAnchorDate, eventScope, cancellationToken)
            : await LoadIncomeForMonthAsync(connection, budgetPlanId, year, month, eventScope, cancellationToken);

        var grossIncome = RoundMoney(incomeItems.Sum(item => item.Amount));

        ExpenseSnapshot expenseSnapshot;
        decimal paidExpensesFromIncome;
        if (payFrequency == "monthly" && periodWindow is not null)
        {
            expenseSnapshot = await LoadPayPeriodExpenseSnapshotAsync(
                connection,
                expensePlanScope.PlanIds,
                expensePlanScope.PlanNamesById,
                budgetPlanId,
                periodWindow,
                year,
                month,
                payDate,
                payFrequency,
                cancellationToken);

            paidExpensesFromIncome = mode == "home_core" || expenseSnapshot.ExpenseIds.Count == 0
                ? 0
                : await LoadPaidExpenseTotalFromIncomeAsync(connection, expenseSnapshot.ExpenseIds, cancellationToken);
        }
        else
        {
            expenseSnapshot = await LoadCalendarMonthExpenseSnapshotAsync(
                connection,
                expensePlanScope.PlanIds,
                expensePlanScope.PlanNamesById,
                budgetPlanId,
                year,
                month,
                cancellationToken);
            paidExpensesFromIncome = expenseSnapshot.PaidExpenses;
        }

        var allocationSnapshot = await LoadAllocationSnapshotAsync(connection, context, budgetPlanId, year, month, cancellationToken);
        var plannedSetAside = RoundMoney(
            allocationSnapshot.MonthlyAllowance
            + allocationSnapshot.MonthlySavingsContribution
            + allocationSnapshot.MonthlyEmergencyContribution
            + allocationSnapshot.MonthlyInvestmentContribution
            + allocationSnapshot.CustomTotal);

        var debtPlan = await ComputeDebtPlanAsync(
            connection,
            budgetPlanId,
            year,
            month,
            periodKey,
            periodWindow,
            payDate,
            payFrequency,
            payAnchorDate,
            includePaidTotals: mode != "home_core",
            cancellationToken);

        var plannedBills = RoundMoney(expenseSnapshot.PlannedExpenses + debtPlan.PlannedDebtPayments);
        var paidBillsSoFar = RoundMoney(expenseSnapshot.PaidExpenses + debtPlan.TotalPaidDebtPayments);
        var remainingExpenseBills = RoundMoney(Math.Max(0, expenseSnapshot.PlannedExpenses - expenseSnapshot.PaidExpenses));
        var remainingDebtBills = RoundMoney(Math.Max(0, debtPlan.PlannedDebtPayments - debtPlan.TotalPaidDebtPayments));
        var remainingBills = RoundMoney(remainingExpenseBills + remainingDebtBills);
        var moneyLeftAfterPlan = RoundMoney(grossIncome - plannedBills - plannedSetAside);
        var spendableIncomeRightNow = RoundMoney(grossIncome - paidExpensesFromIncome - debtPlan.PaidDebtPaymentsFromIncome - plannedSetAside);
        var incomeSacrificePct = grossIncome > 0 ? RoundMetric((plannedSetAside / grossIncome) * 100m) : 0;
        var moneyLeftPctOfGross = grossIncome > 0 ? RoundMetric((moneyLeftAfterPlan / grossIncome) * 100m) : 0;
        var isOnPlan = moneyLeftAfterPlan >= 0;

        return new IncomeMonthResponse
        {
            BudgetPlanId = budgetPlanId,
            Month = month,
            Year = year,
            MonthKey = monthKey,
            PeriodLabel = periodLabel,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            PeriodRangeLabel = periodRangeLabel,
            IncomeItems = incomeItems
                .Select(item => new IncomeMonthIncomeItemResponse
                {
                    Id = item.Id,
                    Name = item.Name,
                    Amount = item.Amount,
                })
                .ToArray(),
            GrossIncome = grossIncome,
            SourceCount = incomeItems.Count,
            PlannedExpenses = expenseSnapshot.PlannedExpenses,
            PaidExpenses = expenseSnapshot.PaidExpenses,
            ExpenseBreakdown = new IncomeMonthExpenseBreakdownResponse
            {
                SelectedPlanExpenses = expenseSnapshot.SelectedPlanExpenses,
                AdditionalPlansExpenses = expenseSnapshot.AdditionalPlansExpenses,
                SelectedPlanPreview = expenseSnapshot.SelectedPlanPreview,
                AdditionalPlansPreview = expenseSnapshot.AdditionalPlansPreview,
            },
            PlannedDebtPayments = debtPlan.PlannedDebtPayments,
            PaidDebtPayments = debtPlan.PaidDebtPaymentsFromIncome,
            MonthlyAllowance = allocationSnapshot.MonthlyAllowance,
            IncomeSacrifice = plannedSetAside,
            SetAsideBreakdown = new IncomeMonthSetAsideBreakdownResponse
            {
                Savings = allocationSnapshot.MonthlySavingsContribution,
                Emergency = allocationSnapshot.MonthlyEmergencyContribution,
                Investments = allocationSnapshot.MonthlyInvestmentContribution,
                Custom = allocationSnapshot.CustomTotal,
            },
            PlannedBills = plannedBills,
            PaidBillsSoFar = paidBillsSoFar,
            RemainingExpenseBills = remainingExpenseBills,
            RemainingDebtBills = remainingDebtBills,
            RemainingBills = remainingBills,
            LeftToPayRightNow = remainingBills,
            MoneyLeftAfterPlan = moneyLeftAfterPlan,
            IncomeLeftRightNow = spendableIncomeRightNow,
            SpendableIncomeRightNow = spendableIncomeRightNow,
            MoneyOutTotal = RoundMoney(plannedBills + plannedSetAside),
            IsOnPlan = isOnPlan,
            IncomeSacrificePct = incomeSacrificePct,
            MoneyLeftPctOfGross = moneyLeftPctOfGross,
            MoneyLeftVsLastMonthPct = null,
            PlanStatusTag = isOnPlan ? "on_plan" : "over_plan",
            PlanStatusDescription = isOnPlan ? "On plan" : "Over plan",
        };
    }

    private static async Task<IncomeMonthContextRow?> LoadContextAsync(
        System.Data.Common.DbConnection connection,
        string budgetPlanId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                bp."id" AS "BudgetPlanId",
                bp."userId" AS "UserId",
                bp."name" AS "PlanName",
                bp."kind" AS "Kind",
                bp."eventDate" AS "EventDate",
                bp."payDate" AS "PayDate",
                bp."monthlyAllowance" AS "MonthlyAllowance",
                bp."monthlySavingsContribution" AS "MonthlySavingsContribution",
                bp."monthlyEmergencyContribution" AS "MonthlyEmergencyContribution",
                bp."monthlyInvestmentContribution" AS "MonthlyInvestmentContribution",
                uop."payFrequency" AS "PayFrequency",
                uop."payAnchorDate" AS "PayAnchorDate"
            FROM "BudgetPlan" bp
            LEFT JOIN "UserOnboardingProfile" uop ON uop."userId" = bp."userId"
            WHERE bp."id" = @BudgetPlanId
            LIMIT 1;
            """;

        return await connection.QuerySingleOrDefaultAsync<IncomeMonthContextRow>(
            new CommandDefinition(sql, new { BudgetPlanId = budgetPlanId }, cancellationToken: cancellationToken));
    }

    private static async Task<PlanScope> ResolveExpensePlanScopeAsync(
        System.Data.Common.DbConnection connection,
        IncomeMonthContextRow context,
        string budgetPlanId,
        string mode,
        CancellationToken cancellationToken)
    {
        if (mode == "home_core" || !string.Equals(context.Kind, "personal", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(context.UserId))
        {
            var singleName = string.IsNullOrWhiteSpace(context.PlanName) ? "My Budget" : context.PlanName!;
            return new PlanScope(
                new[] { budgetPlanId },
                new Dictionary<string, string>(StringComparer.Ordinal) { [budgetPlanId] = singleName });
        }

        const string sql = """
            SELECT
                bp."id" AS "Id",
                bp."name" AS "Name"
            FROM "BudgetPlan" bp
            WHERE bp."userId" = @UserId;
            """;

        var rows = (await connection.QueryAsync<PlanNameRow>(
            new CommandDefinition(sql, new { UserId = context.UserId }, cancellationToken: cancellationToken))).ToArray();

        if (rows.Length == 0)
        {
            var singleName = string.IsNullOrWhiteSpace(context.PlanName) ? "My Budget" : context.PlanName!;
            return new PlanScope(
                new[] { budgetPlanId },
                new Dictionary<string, string>(StringComparer.Ordinal) { [budgetPlanId] = singleName });
        }

        return new PlanScope(
            rows.Select(row => row.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToArray(),
            rows.Where(row => !string.IsNullOrWhiteSpace(row.Id))
                .ToDictionary(row => row.Id, row => string.IsNullOrWhiteSpace(row.Name) ? "Budget plan" : row.Name!, StringComparer.Ordinal));
    }

    private static async Task<IReadOnlyList<IncomeItemRow>> LoadIncomeForAnchorMonthAsync(
        System.Data.Common.DbConnection connection,
        string budgetPlanId,
        int year,
        int month,
        int payDate,
        string payFrequency,
        DateTime? payAnchorDate,
        EventScope? eventScope,
        CancellationToken cancellationToken)
    {
        if (eventScope is not null && IsAfterEventMonth(year, month, eventScope))
        {
            return Array.Empty<IncomeItemRow>();
        }

        var canonicalPeriodKey = GetIncomePeriodKey(year, month, payDate, payFrequency, payAnchorDate);

        const string sql = """
            SELECT
                i."id" AS "Id",
                i."name" AS "Name",
                i."amount" AS "Amount",
                i."month" AS "Month",
                i."year" AS "Year",
                i."periodKey" AS "PeriodKey",
                i."createdAt" AS "CreatedAt",
                i."updatedAt" AS "UpdatedAt"
            FROM "Income" i
            WHERE i."budgetPlanId" = @BudgetPlanId
              AND ((i."year" = @Year AND i."month" = @Month) OR i."periodKey" = @CanonicalPeriodKey)
            ORDER BY i."updatedAt" ASC, i."createdAt" ASC;
            """;

        var rows = (await connection.QueryAsync<IncomeCandidateRow>(
            new CommandDefinition(
                sql,
                new { BudgetPlanId = budgetPlanId, Year = year, Month = month, CanonicalPeriodKey = canonicalPeriodKey },
                cancellationToken: cancellationToken))).ToArray();

        var groupedRows = new Dictionary<string, List<IncomeCandidateRow>>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (row.Year != year || row.Month != month)
            {
                if (!string.Equals(row.PeriodKey, canonicalPeriodKey, StringComparison.Ordinal)) continue;
            }

            var key = NormalizeName(row.Name);
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (!groupedRows.TryGetValue(key, out var bucket))
            {
                bucket = new List<IncomeCandidateRow>();
                groupedRows[key] = bucket;
            }

            bucket.Add(row);
        }

        return groupedRows.Values
            .Select(rowsForName => PickIncomeRowForAnchor(rowsForName, year, month, canonicalPeriodKey))
            .Where(row => row is not null)
            .Select(row => new IncomeItemRow
            {
                Id = row!.Id,
                Name = row.Name,
                Amount = RoundMoney(row.Amount),
            })
            .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<IReadOnlyList<IncomeItemRow>> LoadIncomeForMonthAsync(
        System.Data.Common.DbConnection connection,
        string budgetPlanId,
        int year,
        int month,
        EventScope? eventScope,
        CancellationToken cancellationToken)
    {
        if (eventScope is not null && IsAfterEventMonth(year, month, eventScope))
        {
            return Array.Empty<IncomeItemRow>();
        }

        const string sql = """
            SELECT
                i."id" AS "Id",
                i."name" AS "Name",
                i."amount" AS "Amount",
                i."createdAt" AS "CreatedAt",
                i."updatedAt" AS "UpdatedAt"
            FROM "Income" i
            WHERE i."budgetPlanId" = @BudgetPlanId
              AND i."year" = @Year
              AND i."month" = @Month
            ORDER BY i."updatedAt" ASC, i."createdAt" ASC;
            """;

        var rows = (await connection.QueryAsync<IncomeFlatRow>(
            new CommandDefinition(sql, new { BudgetPlanId = budgetPlanId, Year = year, Month = month }, cancellationToken: cancellationToken))).ToArray();

        var groupedRows = new Dictionary<string, List<IncomeFlatRow>>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var key = NormalizeName(row.Name);
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (!groupedRows.TryGetValue(key, out var bucket))
            {
                bucket = new List<IncomeFlatRow>();
                groupedRows[key] = bucket;
            }

            bucket.Add(row);
        }

        return groupedRows.Values
            .Select(PickCanonicalIncomeRow)
            .Where(row => row is not null)
            .Select(row => new IncomeItemRow
            {
                Id = row!.Id,
                Name = row.Name,
                Amount = RoundMoney(row.Amount),
            })
            .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<ExpenseSnapshot> LoadPayPeriodExpenseSnapshotAsync(
        System.Data.Common.DbConnection connection,
        IReadOnlyList<string> budgetPlanIds,
        IReadOnlyDictionary<string, string> planNamesById,
        string selectedBudgetPlanId,
        PayPeriodWindow periodWindow,
        int anchorYear,
        int anchorMonth,
        int payDate,
        string payFrequency,
        CancellationToken cancellationToken)
    {
        var sourceWindowPairs = BuildSourceWindowPairs(periodWindow.Start, periodWindow.End);
        var sql = BuildPayPeriodExpenseSql(sourceWindowPairs);
        var parameters = new DynamicParameters();
        parameters.Add("BudgetPlanIds", budgetPlanIds.ToArray());

        for (var index = 0; index < sourceWindowPairs.Count; index += 1)
        {
            parameters.Add($"Year{index}", sourceWindowPairs[index].Year);
            parameters.Add($"Month{index}", sourceWindowPairs[index].Month);
        }

        var rows = (await connection.QueryAsync<ExpenseRow>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken))).ToArray();

        var filteredRows = rows
            .GroupBy(row => row.BudgetPlanId, StringComparer.Ordinal)
            .SelectMany(group => FilterPayPeriodExpenses(group, periodWindow, payDate, payFrequency, anchorYear, anchorMonth))
            .ToArray();

        var selectedRows = filteredRows.Where(row => string.Equals(row.BudgetPlanId, selectedBudgetPlanId, StringComparison.Ordinal)).ToArray();
        var additionalRows = filteredRows.Where(row => !string.Equals(row.BudgetPlanId, selectedBudgetPlanId, StringComparison.Ordinal)).ToArray();

        return new ExpenseSnapshot
        {
            PlannedExpenses = RoundMoney(filteredRows.Sum(row => row.Amount)),
            PaidExpenses = RoundMoney(filteredRows.Sum(row => row.PaidAmount)),
            ExpenseIds = filteredRows.Select(row => row.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToArray(),
            SelectedPlanExpenses = RoundMoney(selectedRows.Sum(row => row.Amount)),
            AdditionalPlansExpenses = RoundMoney(additionalRows.Sum(row => row.Amount)),
            SelectedPlanPreview = BuildExpensePreview(selectedRows, planNamesById),
            AdditionalPlansPreview = BuildExpensePreview(additionalRows, planNamesById),
        };
    }

    private static async Task<ExpenseSnapshot> LoadCalendarMonthExpenseSnapshotAsync(
        System.Data.Common.DbConnection connection,
        IReadOnlyList<string> budgetPlanIds,
        IReadOnlyDictionary<string, string> planNamesById,
        string selectedBudgetPlanId,
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                e."id" AS "Id",
                e."name" AS "Name",
                e."budgetPlanId" AS "BudgetPlanId",
                e."amount" AS "Amount",
                e."paidAmount" AS "PaidAmount",
                e."isExtraLoggedExpense" AS "IsExtraLoggedExpense",
                e."paymentSource" AS "PaymentSource"
            FROM "Expense" e
            WHERE e."budgetPlanId" = ANY(@BudgetPlanIds)
              AND e."year" = @Year
              AND e."month" = @Month
              AND COALESCE(e."isAllocation", false) = false
              AND COALESCE(e."isMovedToDebt", false) = false;
            """;

        var rows = (await connection.QueryAsync<ExpenseRow>(
            new CommandDefinition(sql, new { BudgetPlanIds = budgetPlanIds.ToArray(), Year = year, Month = month }, cancellationToken: cancellationToken)))
            .Where(IncludeInPlannedExpenseTotals)
            .ToArray();

        var selectedRows = rows.Where(row => string.Equals(row.BudgetPlanId, selectedBudgetPlanId, StringComparison.Ordinal)).ToArray();
        var additionalRows = rows.Where(row => !string.Equals(row.BudgetPlanId, selectedBudgetPlanId, StringComparison.Ordinal)).ToArray();

        return new ExpenseSnapshot
        {
            PlannedExpenses = RoundMoney(rows.Sum(row => row.Amount)),
            PaidExpenses = RoundMoney(rows.Sum(row => row.PaidAmount)),
            ExpenseIds = rows.Select(row => row.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToArray(),
            SelectedPlanExpenses = RoundMoney(selectedRows.Sum(row => row.Amount)),
            AdditionalPlansExpenses = RoundMoney(additionalRows.Sum(row => row.Amount)),
            SelectedPlanPreview = BuildExpensePreview(selectedRows, planNamesById),
            AdditionalPlansPreview = BuildExpensePreview(additionalRows, planNamesById),
        };
    }

    private static async Task<AllocationSnapshot> LoadAllocationSnapshotAsync(
        System.Data.Common.DbConnection connection,
        IncomeMonthContextRow context,
        string budgetPlanId,
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        const string allocationSql = """
            SELECT
                ma."monthlyAllowance" AS "MonthlyAllowance",
                ma."monthlySavingsContribution" AS "MonthlySavingsContribution",
                ma."monthlyEmergencyContribution" AS "MonthlyEmergencyContribution",
                ma."monthlyInvestmentContribution" AS "MonthlyInvestmentContribution"
            FROM "MonthlyAllocation" ma
            WHERE ma."budgetPlanId" = @BudgetPlanId
              AND ma."year" = @Year
              AND ma."month" = @Month
            LIMIT 1;
            """;

        const string customSql = """
            SELECT
                ad."defaultAmount" AS "DefaultAmount",
                mai."amount" AS "OverrideAmount"
            FROM "AllocationDefinition" ad
            LEFT JOIN "MonthlyAllocationItem" mai
              ON mai."allocationId" = ad."id"
             AND mai."year" = @Year
             AND mai."month" = @Month
            WHERE ad."budgetPlanId" = @BudgetPlanId
              AND ad."isArchived" = false;
            """;

        var allocationOverride = await connection.QuerySingleOrDefaultAsync<AllocationOverrideRow>(
            new CommandDefinition(allocationSql, new { BudgetPlanId = budgetPlanId, Year = year, Month = month }, cancellationToken: cancellationToken));
        var customRows = await connection.QueryAsync<CustomAllocationRow>(
            new CommandDefinition(customSql, new { BudgetPlanId = budgetPlanId, Year = year, Month = month }, cancellationToken: cancellationToken));
        var customTotal = RoundMoney(customRows.Sum(row => row.OverrideAmount ?? row.DefaultAmount ?? 0));

        return new AllocationSnapshot
        {
            MonthlyAllowance = RoundMoney(allocationOverride?.MonthlyAllowance ?? context.MonthlyAllowance ?? 0),
            MonthlySavingsContribution = RoundMoney(allocationOverride?.MonthlySavingsContribution ?? context.MonthlySavingsContribution ?? 0),
            MonthlyEmergencyContribution = RoundMoney(allocationOverride?.MonthlyEmergencyContribution ?? context.MonthlyEmergencyContribution ?? 0),
            MonthlyInvestmentContribution = RoundMoney(allocationOverride?.MonthlyInvestmentContribution ?? context.MonthlyInvestmentContribution ?? 0),
            CustomTotal = customTotal,
        };
    }

    private static async Task<DebtPlanSnapshot> ComputeDebtPlanAsync(
        System.Data.Common.DbConnection connection,
        string budgetPlanId,
        int year,
        int month,
        string? periodKey,
        PayPeriodWindow? periodWindow,
        int payDate,
        string payFrequency,
        DateTime? payAnchorDate,
        bool includePaidTotals,
        CancellationToken cancellationToken)
    {
        const string debtSql = """
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

        var debtRows = (await connection.QueryAsync<DebtRow>(
            new CommandDefinition(debtSql, new { BudgetPlanId = budgetPlanId }, cancellationToken: cancellationToken))).ToArray();

        var overrides = await LoadDebtOverridesAsync(connection, debtRows.Select(row => row.Id).ToArray(), periodKey, cancellationToken);
        var regularDebts = debtRows.Where(row => !string.Equals(row.SourceType, "expense", StringComparison.OrdinalIgnoreCase)).ToArray();

        decimal plannedDebtPayments = 0;
        foreach (var debt in debtRows)
        {
            if (!ShouldIncludeDebtInPlannedPeriod(debt, regularDebts, year, month, periodWindow, payDate, payFrequency, payAnchorDate))
            {
                continue;
            }

            var currentBalance = Math.Max(0, debt.CurrentBalance ?? 0);
            var plannedAmount = overrides.TryGetValue(debt.Id, out var overrideAmount)
                ? Math.Min(currentBalance, Math.Max(0, overrideAmount))
                : ComputeMonthlyPlannedPayment(debt);
            plannedDebtPayments += plannedAmount;
        }

        if (!includePaidTotals)
        {
            return new DebtPlanSnapshot
            {
                PlannedDebtPayments = RoundMoney(plannedDebtPayments),
                TotalPaidDebtPayments = 0,
                PaidDebtPaymentsFromIncome = 0,
            };
        }

        var paymentFilterSql = periodKey is not null
            ? "d.\"budgetPlanId\" = @BudgetPlanId AND (dp.\"periodKey\" = @PeriodKey OR (dp.\"paidAt\" >= @EarlyPaymentStart AND dp.\"paidAt\" < @PeriodStart))"
            : periodWindow is not null
                ? "d.\"budgetPlanId\" = @BudgetPlanId AND dp.\"paidAt\" >= @PeriodStart AND dp.\"paidAt\" <= @PeriodEnd"
                : "d.\"budgetPlanId\" = @BudgetPlanId AND dp.\"year\" = @Year AND dp.\"month\" = @Month";

        var paymentSql = $$"""
            SELECT
                COALESCE(SUM(dp."amount"), 0) AS "TotalPaidAmount",
                COALESCE(SUM(CASE WHEN dp."source"::text = 'income' THEN dp."amount" ELSE 0 END), 0) AS "PaidFromIncome"
            FROM "DebtPayment" dp
            INNER JOIN "Debt" d ON d."id" = dp."debtId"
            WHERE {{paymentFilterSql}};
            """;

        var paymentSummary = await connection.QuerySingleAsync<DebtPaymentSummaryRow>(
            new CommandDefinition(
                paymentSql,
                new
                {
                    BudgetPlanId = budgetPlanId,
                    PeriodKey = periodKey,
                    Year = year,
                    Month = month,
                    PeriodStart = periodWindow?.Start,
                    PeriodEnd = periodWindow?.End,
                    EarlyPaymentStart = periodWindow?.Start.AddDays(-7),
                },
                cancellationToken: cancellationToken));

        return new DebtPlanSnapshot
        {
            PlannedDebtPayments = RoundMoney(plannedDebtPayments),
            TotalPaidDebtPayments = RoundMoney(paymentSummary.TotalPaidAmount),
            PaidDebtPaymentsFromIncome = RoundMoney(paymentSummary.PaidFromIncome),
        };
    }

    private static async Task<Dictionary<string, decimal>> LoadDebtOverridesAsync(
        System.Data.Common.DbConnection connection,
        IReadOnlyList<string> debtIds,
        string? periodKey,
        CancellationToken cancellationToken)
    {
        if (debtIds.Count == 0 || string.IsNullOrWhiteSpace(periodKey))
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
            new CommandDefinition(sql, new { PeriodKey = periodKey, DebtIds = debtIds.ToArray() }, cancellationToken: cancellationToken));

        return rows.ToDictionary(row => row.DebtId, row => RoundMoney(row.Amount), StringComparer.Ordinal);
    }

    private static async Task<decimal> LoadPaidExpenseTotalFromIncomeAsync(
        System.Data.Common.DbConnection connection,
        IReadOnlyList<string> expenseIds,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COALESCE(SUM(ep."amount"), 0)
            FROM "ExpensePayment" ep
            WHERE ep."expenseId" = ANY(@ExpenseIds)
              AND ep."source"::text = 'income';
            """;

        var amount = await connection.ExecuteScalarAsync<decimal?>(
            new CommandDefinition(sql, new { ExpenseIds = expenseIds.ToArray() }, cancellationToken: cancellationToken));

        return RoundMoney(amount ?? 0);
    }

    private static IncomeMonthExpensePreviewResponse BuildExpensePreview(
        IEnumerable<ExpenseRow> expenses,
        IReadOnlyDictionary<string, string> planNamesById)
    {
        var sorted = expenses
            .OrderByDescending(expense => expense.Amount)
            .ThenBy(expense => expense.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var items = sorted.Take(2)
            .Select(expense => new IncomeMonthExpensePreviewItemResponse
            {
                ExpenseId = expense.Id,
                ExpenseName = string.IsNullOrWhiteSpace(expense.Name) ? "Expense" : expense.Name,
                PlanId = expense.BudgetPlanId,
                PlanName = planNamesById.TryGetValue(expense.BudgetPlanId, out var planName) ? planName : "Budget plan",
                Amount = RoundMoney(expense.Amount),
            })
            .ToArray();

        return new IncomeMonthExpensePreviewResponse
        {
            Items = items,
            RemainingCount = Math.Max(0, sorted.Length - items.Length),
        };
    }

    private static bool IncludeInPlannedExpenseTotals(ExpenseRow expense)
    {
        if (!expense.IsExtraLoggedExpense) return true;
        return string.Equals((expense.PaymentSource ?? "income").Trim(), "income", StringComparison.OrdinalIgnoreCase);
    }

    private static List<ExpenseRow> FilterPayPeriodExpenses(
        IEnumerable<ExpenseRow> rows,
        PayPeriodWindow selectedWindow,
        int payDate,
        string payFrequency,
        int anchorYear,
        int anchorMonth)
    {
        var allowedUnscheduledYm = new HashSet<string>(StringComparer.Ordinal)
        {
            $"{selectedWindow.Start.Year}-{selectedWindow.Start.Month}",
            $"{selectedWindow.End.Year}-{selectedWindow.End.Month}",
        };

        var seen = new Dictionary<string, RankedExpense>(StringComparer.Ordinal);
        foreach (var expense in rows)
        {
            if (expense.IsAllocation || expense.IsMovedToDebt || !IncludeInPlannedExpenseTotals(expense)) continue;

            var series = NormalizeSeriesOrName(expense.SeriesKey, expense.Name);
            var amount = RoundMoney(expense.Amount);

            if (expense.DueDate is not null)
            {
                var dueIso = ResolveEffectiveDueDateIso(expense.Year, expense.Month, expense.DueDate, payDate);
                if (dueIso is null) continue;

                var due = ParseIsoDate(dueIso);
                if (due is null || due < selectedWindow.Start || due > selectedWindow.End) continue;

                var rank = expense.Year == due.Value.Year && expense.Month == due.Value.Month ? 0 : 1;
                var dueKey = $"{series}|{dueIso}|{amount.ToString(CultureInfo.InvariantCulture)}";
                TryStoreBestExpense(seen, dueKey, expense, rank);
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
                e."budgetPlanId" AS "BudgetPlanId",
                e."amount" AS "Amount",
                e."paidAmount" AS "PaidAmount",
                e."seriesKey" AS "SeriesKey",
                e."periodKey" AS "PeriodKey",
                e."dueDate" AS "DueDate",
                e."year" AS "Year",
                e."month" AS "Month",
                e."isAllocation" AS "IsAllocation",
                e."isMovedToDebt" AS "IsMovedToDebt",
                e."isExtraLoggedExpense" AS "IsExtraLoggedExpense",
                e."paymentSource" AS "PaymentSource"
            FROM "Expense" e
            WHERE e."budgetPlanId" = ANY(@BudgetPlanIds)
              AND ({{pairFilters}})
              AND COALESCE(e."isAllocation", false) = false
              AND COALESCE(e."isMovedToDebt", false) = false
            ORDER BY e."budgetPlanId" ASC, e."year" ASC, e."month" ASC, e."createdAt" ASC;
            """;
    }

    private static List<(int Year, int Month)> BuildSourceWindowPairs(DateTime start, DateTime end)
        => new[]
        {
            (start.Year, start.Month),
            (end.Year, end.Month),
            (start.AddMonths(-1).Year, start.AddMonths(-1).Month),
            (end.AddMonths(1).Year, end.AddMonths(1).Month),
        }.Distinct().ToList();

    private static EventScope? ResolveEventScope(string? kind, DateTime? eventDate)
    {
        if (eventDate is null) return null;
        if (!string.Equals(kind, "holiday", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(kind, "carnival", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new EventScope(eventDate.Value.Year, eventDate.Value.Month);
    }

    private static bool IsAfterEventMonth(int year, int month, EventScope scope)
    {
        if (year > scope.EventYear) return true;
        if (year < scope.EventYear) return false;
        return month > scope.EventMonth;
    }

    private static string GetIncomePeriodKey(int year, int month, int payDate, string payFrequency, DateTime? payAnchorDate)
    {
        if (payFrequency == "monthly")
        {
            return ToIsoDate(ClampDayUtc(year, month - 2, payDate));
        }

        var period = BuildPayPeriodFromMonthAnchor(year, month, payDate, payFrequency, payAnchorDate);
        return ToIsoDate(period.Start);
    }

    private static IncomeCandidateRow? PickIncomeRowForAnchor(IEnumerable<IncomeCandidateRow> rows, int year, int month, string canonicalPeriodKey)
    {
        var rowList = rows.ToArray();
        var canonicalRows = rowList.Where(row => string.Equals(row.PeriodKey, canonicalPeriodKey, StringComparison.Ordinal)).ToArray();
        if (canonicalRows.Length > 0) return PickCanonicalIncomeRow(canonicalRows);

        var monthRows = rowList.Where(row => row.Year == year && row.Month == month).ToArray();
        if (monthRows.Length > 0) return PickCanonicalIncomeRow(monthRows);

        return PickCanonicalIncomeRow(rowList);
    }

    private static T? PickCanonicalIncomeRow<T>(IEnumerable<T> rows)
        where T : IncomeRowBase
    {
        var rowList = rows.ToArray();
        if (rowList.Length == 0) return null;

        T? best = null;
        foreach (var row in rowList)
        {
            if (best is null)
            {
                best = row;
                continue;
            }

            var bestLegacy = IsAllCapsName(best.Name);
            var rowLegacy = IsAllCapsName(row.Name);
            if (bestLegacy != rowLegacy)
            {
                best = rowLegacy ? best : row;
                continue;
            }

            if (row.CreatedAt != best.CreatedAt)
            {
                best = row.CreatedAt < best.CreatedAt ? row : best;
                continue;
            }

            if (row.UpdatedAt != best.UpdatedAt)
            {
                best = row.UpdatedAt < best.UpdatedAt ? row : best;
            }
        }

        return best;
    }

    private static bool IsAllCapsName(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed)) return false;
        var letters = new string(trimmed.Where(char.IsLetter).ToArray());
        if (string.IsNullOrEmpty(letters)) return false;
        return string.Equals(letters, letters.ToUpperInvariant(), StringComparison.Ordinal);
    }

    private static string NormalizeName(string? value)
        => string.Join(' ', (value ?? string.Empty).Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static bool ShouldIncludeDebtInPlannedPeriod(
        DebtRow debt,
        IReadOnlyList<DebtRow> regularDebts,
        int year,
        int month,
        PayPeriodWindow? periodWindow,
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

        if (periodWindow is not null)
        {
            return dueDate.Value >= periodWindow.Start && dueDate.Value <= periodWindow.End;
        }

        return dueDate.Value.Year == year && dueDate.Value.Month == month;
    }

    private static decimal ComputeMonthlyPlannedPayment(DebtRow debt)
    {
        var currentBalance = Math.Max(0, debt.CurrentBalance ?? 0);
        if (currentBalance <= 0) return 0;

        var amount = Math.Max(0, debt.Amount ?? 0);
        var monthlyMinimum = Math.Max(0, debt.MonthlyMinimum ?? 0);
        var installmentMonths = debt.InstallmentMonths.GetValueOrDefault();
        var safeInstallmentMonths = installmentMonths > 0 ? installmentMonths : 0;
        var initialBalance = Math.Max(0, debt.InitialBalance ?? 0);
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

        return Math.Min(currentBalance, RoundMoney(Math.Max(0, planned)));
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
            if ((debt.CurrentBalance ?? 0) <= 0) continue;

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

        var suffixMatch = System.Text.RegularExpressions.Regex.Match(value, @"\s*\((\d{4}-\d{2})(?:\s+\d{4}(?:-\d{2})?)?\)\s*$");
        if (suffixMatch.Success)
        {
            value = value[..suffixMatch.Index].Trim();
        }

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
        return string.Join(' ', normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
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

    private static string NormalizeSeriesOrName(string? seriesKey, string? name)
        => string.Join(' ', (seriesKey ?? name ?? string.Empty).Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string? ResolveEffectiveDueDateIso(int year, int month, DateTime? dueDate, int payDate)
    {
        if (dueDate is not null)
        {
            return ToIsoDate(StartOfUtcDay(dueDate.Value));
        }

        if (month is < 1 or > 12) return null;
        return ToIsoDate(ClampDayUtc(year, month - 1, payDate));
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

        var legacyPeriodKey = ToIsoDate(new DateTime(anchorYear, anchorMonth, 1, 0, 0, 0, DateTimeKind.Utc));
        return string.Equals(normalized, legacyPeriodKey, StringComparison.Ordinal) ? canonicalPeriodKey : null;
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
        => payDate >= 1 ? payDate : 27;

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

    private static DateTime? ParseIsoDate(string iso)
    {
        if (!DateTime.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return null;
        }

        return StartOfUtcDay(parsed);
    }

    private static decimal RoundMoney(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal RoundMetric(decimal value) => decimal.Round(value, 1, MidpointRounding.AwayFromZero);

    private static string ToIsoDate(DateTime value) => StartOfUtcDay(value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private abstract class IncomeRowBase
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    private sealed class IncomeMonthContextRow
    {
        public string BudgetPlanId { get; set; } = string.Empty;
        public string? UserId { get; set; }
        public string? PlanName { get; set; }
        public string? Kind { get; set; }
        public DateTime? EventDate { get; set; }
        public int PayDate { get; set; }
        public decimal? MonthlyAllowance { get; set; }
        public decimal? MonthlySavingsContribution { get; set; }
        public decimal? MonthlyEmergencyContribution { get; set; }
        public decimal? MonthlyInvestmentContribution { get; set; }
        public string? PayFrequency { get; set; }
        public DateTime? PayAnchorDate { get; set; }
    }

    private sealed class PlanNameRow
    {
        public string Id { get; set; } = string.Empty;
        public string? Name { get; set; }
    }

    private sealed class PlanScope(IReadOnlyList<string> planIds, IReadOnlyDictionary<string, string> planNamesById)
    {
        public IReadOnlyList<string> PlanIds { get; } = planIds;
        public IReadOnlyDictionary<string, string> PlanNamesById { get; } = planNamesById;
    }

    private sealed class IncomeCandidateRow : IncomeRowBase
    {
        public int Month { get; set; }
        public int Year { get; set; }
        public string? PeriodKey { get; set; }
    }

    private sealed class IncomeFlatRow : IncomeRowBase
    {
    }

    private sealed class IncomeItemRow
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public decimal Amount { get; init; }
    }

    private sealed class ExpenseRow
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string BudgetPlanId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public decimal PaidAmount { get; set; }
        public string? SeriesKey { get; set; }
        public string? PeriodKey { get; set; }
        public DateTime? DueDate { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public bool IsAllocation { get; set; }
        public bool IsMovedToDebt { get; set; }
        public bool IsExtraLoggedExpense { get; set; }
        public string? PaymentSource { get; set; }
    }

    private sealed class RankedExpense
    {
        public ExpenseRow Expense { get; init; } = new();
        public int Rank { get; init; }
    }

    private sealed class ExpenseSnapshot
    {
        public decimal PlannedExpenses { get; init; }
        public decimal PaidExpenses { get; init; }
        public IReadOnlyList<string> ExpenseIds { get; init; } = Array.Empty<string>();
        public decimal SelectedPlanExpenses { get; init; }
        public decimal AdditionalPlansExpenses { get; init; }
        public IncomeMonthExpensePreviewResponse SelectedPlanPreview { get; init; } = new();
        public IncomeMonthExpensePreviewResponse AdditionalPlansPreview { get; init; } = new();
    }

    private sealed class AllocationOverrideRow
    {
        public decimal? MonthlyAllowance { get; set; }
        public decimal? MonthlySavingsContribution { get; set; }
        public decimal? MonthlyEmergencyContribution { get; set; }
        public decimal? MonthlyInvestmentContribution { get; set; }
    }

    private sealed class CustomAllocationRow
    {
        public decimal? DefaultAmount { get; set; }
        public decimal? OverrideAmount { get; set; }
    }

    private sealed class AllocationSnapshot
    {
        public decimal MonthlyAllowance { get; init; }
        public decimal MonthlySavingsContribution { get; init; }
        public decimal MonthlyEmergencyContribution { get; init; }
        public decimal MonthlyInvestmentContribution { get; init; }
        public decimal CustomTotal { get; init; }
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

    private sealed class DebtPaymentSummaryRow
    {
        public decimal TotalPaidAmount { get; set; }
        public decimal PaidFromIncome { get; set; }
    }

    private sealed class DebtPlanSnapshot
    {
        public decimal PlannedDebtPayments { get; init; }
        public decimal TotalPaidDebtPayments { get; init; }
        public decimal PaidDebtPaymentsFromIncome { get; init; }
    }

    private sealed class PayPeriodWindow
    {
        public DateTime Start { get; init; }
        public DateTime End { get; init; }
    }

    private sealed record EventScope(int EventYear, int EventMonth);
}