using System.Globalization;
using System.Text;
using System.Text.Json;
using BudgetinCheck.Api.Features.Common;
using BudgetinCheck.Api.Infrastructure.Auth;
using BudgetinCheck.Api.Infrastructure.Data;
using Dapper;

namespace BudgetinCheck.Api.Features.Settings;

internal static class SettingsEndpoints
{
    public static async Task<IResult> GetAsync(
        HttpRequest request,
        CurrentSessionResolver sessionResolver,
        BudgetDbConnectionFactory connectionFactory,
        CancellationToken cancellationToken)
    {
        var sessionResolution = await sessionResolver.ResolveAsync(request, cancellationToken);
        if (!sessionResolution.IsSuccess)
        {
            return sessionResolution.ToResult();
        }

        var requestedBudgetPlanId = request.Query["budgetPlanId"].ToString();
        var budgetPlanId = sessionResolution.Session!.ResolveOwnedBudgetPlanId(requestedBudgetPlanId);
        if (budgetPlanId is null)
        {
            return BffResults.BudgetPlanNotFound();
        }

        try
        {
            await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

            var plan = await GetPlanSettingsAsync(connection, budgetPlanId, cancellationToken);
            if (plan is null)
            {
                return BffResults.BudgetPlanNotFound();
            }

            var profileMeta = await GetProfileMetaAsync(connection, sessionResolution.Session.UserId, cancellationToken);
            return Results.Json(ToSettingsPayload(plan, profileMeta));
        }
        catch
        {
            return BffResults.InternalServerError("Failed to fetch settings");
        }
    }

    public static async Task<IResult> PatchAsync(
        HttpRequest request,
        CurrentSessionResolver sessionResolver,
        BudgetDbConnectionFactory connectionFactory,
        CancellationToken cancellationToken)
    {
        var sessionResolution = await sessionResolver.ResolveAsync(request, cancellationToken);
        if (!sessionResolution.IsSuccess)
        {
            return sessionResolution.ToResult();
        }

        var parsedBody = await TryReadJsonBodyAsync(request, cancellationToken);
        if (!parsedBody.Success)
        {
            return parsedBody.ErrorResult!;
        }

        var body = parsedBody.Body;
        var requestedBudgetPlanId = ReadStringProperty(body, "budgetPlanId") ?? request.Query["budgetPlanId"].ToString();
        if (string.IsNullOrWhiteSpace(requestedBudgetPlanId))
        {
            return BffResults.BadRequest("budgetPlanId is required");
        }

        var budgetPlanId = sessionResolution.Session!.ResolveOwnedBudgetPlanId(requestedBudgetPlanId);
        if (budgetPlanId is null)
        {
            return BffResults.BudgetPlanNotFound();
        }

        try
        {
            await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

            var current = await GetPlanSettingsAsync(connection, budgetPlanId, cancellationToken);
            if (current is null)
            {
                return BffResults.BudgetPlanNotFound();
            }

            var hasPayDate = TryReadIntProperty(body, "payDate", out var payDate);
            if (hasPayDate && payDate is null)
            {
                return BffResults.BadRequest("payDate must be a valid number");
            }

            var hasMonthlyAllowance = TryReadDecimalProperty(body, "monthlyAllowance", out var monthlyAllowance);
            if (hasMonthlyAllowance && monthlyAllowance is null)
            {
                return BffResults.BadRequest("monthlyAllowance must be a valid number");
            }

            var hasSavingsBalance = TryReadDecimalProperty(body, "savingsBalance", out var savingsBalance);
            if (hasSavingsBalance && savingsBalance is null)
            {
                return BffResults.BadRequest("savingsBalance must be a valid number");
            }

            var hasEmergencyBalance = TryReadDecimalProperty(body, "emergencyBalance", out var emergencyBalance);
            if (hasEmergencyBalance && emergencyBalance is null)
            {
                return BffResults.BadRequest("emergencyBalance must be a valid number");
            }

            var hasInvestmentBalance = TryReadDecimalProperty(body, "investmentBalance", out var investmentBalance);
            if (hasInvestmentBalance && investmentBalance is null)
            {
                return BffResults.BadRequest("investmentBalance must be a valid number");
            }

            var hasAdditionalSavings = TryReadDecimalProperty(body, "additionalSavingsBalance", out var additionalSavings);
            if (hasAdditionalSavings)
            {
                if (additionalSavings is null || additionalSavings <= 0)
                {
                    return BffResults.BadRequest("additionalSavingsBalance must be a number greater than 0");
                }

                if (hasSavingsBalance)
                {
                    return BffResults.BadRequest("Use savingsBalance or additionalSavingsBalance, not both");
                }
            }

            var hasAdditionalEmergency = TryReadDecimalProperty(body, "additionalEmergencyBalance", out var additionalEmergency);
            if (hasAdditionalEmergency)
            {
                if (additionalEmergency is null || additionalEmergency <= 0)
                {
                    return BffResults.BadRequest("additionalEmergencyBalance must be a number greater than 0");
                }

                if (hasEmergencyBalance)
                {
                    return BffResults.BadRequest("Use emergencyBalance or additionalEmergencyBalance, not both");
                }
            }

            var hasAdditionalInvestment = TryReadDecimalProperty(body, "additionalInvestmentBalance", out var additionalInvestment);
            if (hasAdditionalInvestment)
            {
                if (additionalInvestment is null || additionalInvestment <= 0)
                {
                    return BffResults.BadRequest("additionalInvestmentBalance must be a number greater than 0");
                }

                if (hasInvestmentBalance)
                {
                    return BffResults.BadRequest("Use investmentBalance or additionalInvestmentBalance, not both");
                }
            }

            var hasMonthlySavingsContribution = TryReadDecimalProperty(body, "monthlySavingsContribution", out var monthlySavingsContribution);
            if (hasMonthlySavingsContribution && monthlySavingsContribution is null)
            {
                return BffResults.BadRequest("monthlySavingsContribution must be a valid number");
            }

            var hasMonthlyEmergencyContribution = TryReadDecimalProperty(body, "monthlyEmergencyContribution", out var monthlyEmergencyContribution);
            if (hasMonthlyEmergencyContribution && monthlyEmergencyContribution is null)
            {
                return BffResults.BadRequest("monthlyEmergencyContribution must be a valid number");
            }

            var hasMonthlyInvestmentContribution = TryReadDecimalProperty(body, "monthlyInvestmentContribution", out var monthlyInvestmentContribution);
            if (hasMonthlyInvestmentContribution && monthlyInvestmentContribution is null)
            {
                return BffResults.BadRequest("monthlyInvestmentContribution must be a valid number");
            }

            var hasBudgetStrategy = TryReadStringProperty(body, "budgetStrategy", out var budgetStrategy);
            var hasCountry = TryReadStringProperty(body, "country", out var country);
            var hasLanguage = TryReadStringProperty(body, "language", out var language);
            var hasCurrency = TryReadStringProperty(body, "currency", out var currency);

            var hasBudgetHorizonYears = TryReadIntProperty(body, "budgetHorizonYears", out var budgetHorizonYears);
            if (hasBudgetHorizonYears && budgetHorizonYears is null)
            {
                return BffResults.BadRequest("budgetHorizonYears must be a valid number");
            }

            var hasIncomeDistributeFullYearDefault = TryReadBoolProperty(body, "incomeDistributeFullYearDefault", out var incomeDistributeFullYearDefault);
            if (hasIncomeDistributeFullYearDefault && incomeDistributeFullYearDefault is null)
            {
                return BffResults.BadRequest("incomeDistributeFullYearDefault must be true or false");
            }

            var hasIncomeDistributeHorizonDefault = TryReadBoolProperty(body, "incomeDistributeHorizonDefault", out var incomeDistributeHorizonDefault);
            if (hasIncomeDistributeHorizonDefault && incomeDistributeHorizonDefault is null)
            {
                return BffResults.BadRequest("incomeDistributeHorizonDefault must be true or false");
            }

            var hasPayFrequency = body.TryGetProperty("payFrequency", out var payFrequencyElement);
            var hasBillFrequency = body.TryGetProperty("billFrequency", out _);
            var hasPayAnchorDate = body.TryGetProperty("payAnchorDate", out var payAnchorDateElement);
            var normalizedPayFrequency = hasPayFrequency ? NormalizePayFrequency(ReadStringLike(payFrequencyElement)) : null;
            DateTime? normalizedPayAnchorDate = null;
            if (hasPayAnchorDate && !TryParseDateOnlyLike(payAnchorDateElement, out normalizedPayAnchorDate))
            {
                return BffResults.BadRequest("payAnchorDate must be a valid date");
            }

            var hasHomepageGoalIds = TryReadHomepageGoalIds(body, out var requestedHomepageGoalIds);
            string[]? homepageGoalIds = null;
            if (hasHomepageGoalIds)
            {
                if (requestedHomepageGoalIds.Length == 0)
                {
                    homepageGoalIds = [];
                }
                else
                {
                    var ownedIds = await connection.QueryAsync<string>(
                        new CommandDefinition(
                            @"
SELECT ""id""
FROM ""Goal""
WHERE ""budgetPlanId"" = @BudgetPlanId
  AND ""id"" = ANY(@GoalIds)",
                            new { BudgetPlanId = budgetPlanId, GoalIds = requestedHomepageGoalIds },
                            cancellationToken: cancellationToken));

                    var allowed = new HashSet<string>(ownedIds, StringComparer.Ordinal);
                    homepageGoalIds = requestedHomepageGoalIds.Where(id => allowed.Contains(id)).ToArray();
                }
            }

            var setClauses = new List<string>();
            var parameters = new DynamicParameters();
            parameters.Add("BudgetPlanId", budgetPlanId);

            var changedBalances = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

            if (hasPayDate && payDate is not null)
            {
                setClauses.Add("\"payDate\" = @PayDate");
                parameters.Add("PayDate", payDate.Value);
            }

            if (hasMonthlyAllowance && monthlyAllowance is not null)
            {
                setClauses.Add("\"monthlyAllowance\" = @MonthlyAllowance");
                parameters.Add("MonthlyAllowance", monthlyAllowance.Value);
            }

            if (hasSavingsBalance || hasAdditionalSavings)
            {
                var nextSavings = hasSavingsBalance ? savingsBalance!.Value : current.SavingsBalance + additionalSavings!.Value;
                setClauses.Add("\"savingsBalance\" = @SavingsBalance");
                parameters.Add("SavingsBalance", nextSavings);
                changedBalances["savings"] = nextSavings;
            }

            // Only write emergency balance when explicitly requested by emergency fields.
            if (hasEmergencyBalance || hasAdditionalEmergency)
            {
                var nextEmergency = hasEmergencyBalance ? emergencyBalance!.Value : current.EmergencyBalance + additionalEmergency!.Value;
                setClauses.Add("\"emergencyBalance\" = @EmergencyBalance");
                parameters.Add("EmergencyBalance", nextEmergency);
                changedBalances["emergency"] = nextEmergency;
            }

            if (hasInvestmentBalance || hasAdditionalInvestment)
            {
                var nextInvestment = hasInvestmentBalance ? investmentBalance!.Value : current.InvestmentBalance + additionalInvestment!.Value;
                setClauses.Add("\"investmentBalance\" = @InvestmentBalance");
                parameters.Add("InvestmentBalance", nextInvestment);
                changedBalances["investment"] = nextInvestment;
            }

            if (hasMonthlySavingsContribution && monthlySavingsContribution is not null)
            {
                setClauses.Add("\"monthlySavingsContribution\" = @MonthlySavingsContribution");
                parameters.Add("MonthlySavingsContribution", monthlySavingsContribution.Value);
            }

            if (hasMonthlyEmergencyContribution && monthlyEmergencyContribution is not null)
            {
                setClauses.Add("\"monthlyEmergencyContribution\" = @MonthlyEmergencyContribution");
                parameters.Add("MonthlyEmergencyContribution", monthlyEmergencyContribution.Value);
            }

            if (hasMonthlyInvestmentContribution && monthlyInvestmentContribution is not null)
            {
                setClauses.Add("\"monthlyInvestmentContribution\" = @MonthlyInvestmentContribution");
                parameters.Add("MonthlyInvestmentContribution", monthlyInvestmentContribution.Value);
            }

            if (hasBudgetStrategy)
            {
                setClauses.Add("\"budgetStrategy\" = CAST(@BudgetStrategy AS \"BudgetStrategy\")");
                parameters.Add("BudgetStrategy", budgetStrategy!);
            }

            if (hasBudgetHorizonYears && budgetHorizonYears is not null)
            {
                setClauses.Add("\"budgetHorizonYears\" = @BudgetHorizonYears");
                parameters.Add("BudgetHorizonYears", Math.Max(1, budgetHorizonYears.Value));
            }

            if (hasCountry)
            {
                setClauses.Add("\"country\" = @Country");
                parameters.Add("Country", country!);
            }

            if (hasLanguage)
            {
                setClauses.Add("\"language\" = @Language");
                parameters.Add("Language", language!);
            }

            if (hasCurrency)
            {
                setClauses.Add("\"currency\" = @Currency");
                parameters.Add("Currency", currency!);
            }

            if (hasIncomeDistributeFullYearDefault && incomeDistributeFullYearDefault is not null)
            {
                setClauses.Add("\"incomeDistributeFullYearDefault\" = @IncomeDistributeFullYearDefault");
                parameters.Add("IncomeDistributeFullYearDefault", incomeDistributeFullYearDefault.Value);
            }

            if (hasIncomeDistributeHorizonDefault && incomeDistributeHorizonDefault is not null)
            {
                setClauses.Add("\"incomeDistributeHorizonDefault\" = @IncomeDistributeHorizonDefault");
                parameters.Add("IncomeDistributeHorizonDefault", incomeDistributeHorizonDefault.Value);
            }

            if (hasHomepageGoalIds)
            {
                setClauses.Add("\"homepageGoalIds\" = @HomepageGoalIds");
                parameters.Add("HomepageGoalIds", homepageGoalIds ?? []);
            }

            if (setClauses.Count == 0 && !hasPayFrequency && !hasBillFrequency && !hasPayAnchorDate)
            {
                return BffResults.BadRequest("No valid fields to update");
            }

            PlanSettingsRow updatedPlan = current;
            if (setClauses.Count > 0)
            {
                var updateSql = new StringBuilder();
                updateSql.AppendLine("UPDATE \"BudgetPlan\"");
                updateSql.AppendLine("SET");
                updateSql.Append("    ");
                updateSql.Append(string.Join(",\n    ", setClauses));
                updateSql.AppendLine();
                updateSql.AppendLine("WHERE \"id\" = @BudgetPlanId");
                updateSql.AppendLine("RETURNING");
                updateSql.AppendLine("    \"id\" AS \"Id\",");
                updateSql.AppendLine("    \"payDate\" AS \"PayDate\",");
                updateSql.AppendLine("    \"monthlyAllowance\" AS \"MonthlyAllowance\",");
                updateSql.AppendLine("    \"savingsBalance\" AS \"SavingsBalance\",");
                updateSql.AppendLine("    \"emergencyBalance\" AS \"EmergencyBalance\",");
                updateSql.AppendLine("    \"investmentBalance\" AS \"InvestmentBalance\",");
                updateSql.AppendLine("    \"monthlySavingsContribution\" AS \"MonthlySavingsContribution\",");
                updateSql.AppendLine("    \"monthlyEmergencyContribution\" AS \"MonthlyEmergencyContribution\",");
                updateSql.AppendLine("    \"monthlyInvestmentContribution\" AS \"MonthlyInvestmentContribution\",");
                updateSql.AppendLine("    \"budgetStrategy\"::text AS \"BudgetStrategy\",");
                updateSql.AppendLine("    \"budgetHorizonYears\" AS \"BudgetHorizonYears\",");
                updateSql.AppendLine("    \"incomeDistributeFullYearDefault\" AS \"IncomeDistributeFullYearDefault\",");
                updateSql.AppendLine("    \"incomeDistributeHorizonDefault\" AS \"IncomeDistributeHorizonDefault\",");
                updateSql.AppendLine("    \"homepageGoalIds\" AS \"HomepageGoalIds\",");
                updateSql.AppendLine("    \"country\" AS \"Country\",");
                updateSql.AppendLine("    \"language\" AS \"Language\",");
                updateSql.AppendLine("    \"currency\" AS \"Currency\";");

                var row = await connection.QuerySingleOrDefaultAsync<PlanSettingsRow>(
                    new CommandDefinition(
                        updateSql.ToString(),
                        parameters,
                        cancellationToken: cancellationToken));

                if (row is null)
                {
                    return BffResults.BudgetPlanNotFound();
                }

                updatedPlan = row;
            }

            if (changedBalances.Count > 0)
            {
                await SyncGoalCurrentAmountsFromBalancesAsync(connection, budgetPlanId, changedBalances, cancellationToken);
            }

            if (hasPayFrequency || hasPayAnchorDate)
            {
                await UpsertCadenceForUserAsync(
                    connection,
                    sessionResolution.Session.UserId,
                    hasPayFrequency,
                    hasPayAnchorDate,
                    normalizedPayFrequency,
                    normalizedPayAnchorDate,
                    cancellationToken);
            }

            var profileMeta = await GetProfileMetaAsync(connection, sessionResolution.Session.UserId, cancellationToken);
            return Results.Json(ToSettingsPayload(updatedPlan, profileMeta));
        }
        catch
        {
            return BffResults.InternalServerError("Failed to update settings");
        }
    }

    private static async Task<PlanSettingsRow?> GetPlanSettingsAsync(
        System.Data.IDbConnection connection,
        string budgetPlanId,
        CancellationToken cancellationToken)
    {
        return await connection.QuerySingleOrDefaultAsync<PlanSettingsRow>(
            new CommandDefinition(
                @"
SELECT
    ""id"" AS ""Id"",
    ""payDate"" AS ""PayDate"",
    ""monthlyAllowance"" AS ""MonthlyAllowance"",
    ""savingsBalance"" AS ""SavingsBalance"",
    ""emergencyBalance"" AS ""EmergencyBalance"",
    ""investmentBalance"" AS ""InvestmentBalance"",
    ""monthlySavingsContribution"" AS ""MonthlySavingsContribution"",
    ""monthlyEmergencyContribution"" AS ""MonthlyEmergencyContribution"",
    ""monthlyInvestmentContribution"" AS ""MonthlyInvestmentContribution"",
    ""budgetStrategy""::text AS ""BudgetStrategy"",
    ""budgetHorizonYears"" AS ""BudgetHorizonYears"",
    ""incomeDistributeFullYearDefault"" AS ""IncomeDistributeFullYearDefault"",
    ""incomeDistributeHorizonDefault"" AS ""IncomeDistributeHorizonDefault"",
    ""homepageGoalIds"" AS ""HomepageGoalIds"",
    ""country"" AS ""Country"",
    ""language"" AS ""Language"",
    ""currency"" AS ""Currency""
FROM ""BudgetPlan""
WHERE ""id"" = @BudgetPlanId
LIMIT 1",
                new { BudgetPlanId = budgetPlanId },
                cancellationToken: cancellationToken));
    }

    private static async Task<ProfileMetaRow?> GetProfileMetaAsync(
        System.Data.IDbConnection connection,
        string userId,
        CancellationToken cancellationToken)
    {
        return await connection.QuerySingleOrDefaultAsync<ProfileMetaRow>(
            new CommandDefinition(
                @"
SELECT
    u.""createdAt"" AS ""AccountCreatedAt"",
    p.""completedAt"" AS ""SetupCompletedAt"",
    p.""updatedAt"" AS ""SetupUpdatedAt"",
    p.""status""::text AS ""SetupStatus"",
    p.""payFrequency"" AS ""PayFrequency"",
    p.""billFrequency"" AS ""BillFrequency"",
    p.""payAnchorDate"" AS ""PayAnchorDate""
FROM ""User"" u
LEFT JOIN ""UserOnboardingProfile"" p ON p.""userId"" = u.""id""
WHERE u.""id"" = @UserId
LIMIT 1",
                new { UserId = userId },
                cancellationToken: cancellationToken));
    }

    private static async Task UpsertCadenceForUserAsync(
        System.Data.IDbConnection connection,
        string userId,
        bool hasPayFrequency,
        bool hasPayAnchorDate,
        string? payFrequency,
        DateTime? payAnchorDate,
        CancellationToken cancellationToken)
    {
        var updatedRows = await connection.ExecuteAsync(
            new CommandDefinition(
                @"
UPDATE ""UserOnboardingProfile""
SET
    ""payFrequency"" = CASE WHEN @HasPayFrequency THEN @PayFrequency ELSE ""payFrequency"" END,
    ""billFrequency"" = CASE WHEN @HasPayFrequency THEN @BillFrequency ELSE ""billFrequency"" END,
    ""payAnchorDate"" = CASE WHEN @HasPayAnchorDate THEN @PayAnchorDate ELSE ""payAnchorDate"" END,
    ""updatedAt"" = NOW()
WHERE ""userId"" = @UserId",
                new
                {
                    UserId = userId,
                    HasPayFrequency = hasPayFrequency,
                    HasPayAnchorDate = hasPayAnchorDate,
                    PayFrequency = payFrequency,
                    BillFrequency = hasPayFrequency ? DeriveBillFrequencyFromPayFrequency(payFrequency) : null,
                    PayAnchorDate = payAnchorDate,
                },
                cancellationToken: cancellationToken));

        if (updatedRows > 0)
        {
            return;
        }

        await connection.ExecuteAsync(
            new CommandDefinition(
                @"
INSERT INTO ""UserOnboardingProfile"" (
    ""id"",
    ""userId"",
    ""status"",
    ""payFrequency"",
    ""billFrequency"",
    ""payAnchorDate"",
    ""createdAt"",
    ""updatedAt""
)
VALUES (
    @Id,
    @UserId,
    CAST('started' AS ""OnboardingStatus""),
    @PayFrequency,
    @BillFrequency,
    @PayAnchorDate,
    NOW(),
    NOW()
)",
                new
                {
                    Id = $"dotnet_{Guid.NewGuid():N}",
                    UserId = userId,
                    PayFrequency = hasPayFrequency ? payFrequency : null,
                    BillFrequency = hasPayFrequency ? DeriveBillFrequencyFromPayFrequency(payFrequency) : null,
                    PayAnchorDate = hasPayAnchorDate ? payAnchorDate : null,
                },
                cancellationToken: cancellationToken));
    }

    private static async Task SyncGoalCurrentAmountsFromBalancesAsync(
        System.Data.IDbConnection connection,
        string budgetPlanId,
        IReadOnlyDictionary<string, decimal> balances,
        CancellationToken cancellationToken)
    {
        var categories = balances.Keys
            .Where(key => key is "savings" or "emergency" or "investment")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (categories.Length == 0)
        {
            return;
        }

        var goals = (await connection.QueryAsync<GoalSyncRow>(
            new CommandDefinition(
                @"
SELECT
    ""id"" AS ""Id"",
    ""title"" AS ""Title"",
    ""category""::text AS ""Category""
FROM ""Goal""
WHERE ""budgetPlanId"" = @BudgetPlanId
  AND ""category""::text = ANY(@Categories)",
                new
                {
                    BudgetPlanId = budgetPlanId,
                    Categories = categories,
                },
                cancellationToken: cancellationToken))).ToArray();

        if (goals.Length == 0)
        {
            return;
        }

        foreach (var category in categories)
        {
            if (!balances.TryGetValue(category, out var amount))
            {
                continue;
            }

            var goalId = PickGoalIdForCategory(goals, category);
            if (goalId is null)
            {
                continue;
            }

            await connection.ExecuteAsync(
                new CommandDefinition(
                    @"
UPDATE ""Goal""
SET ""currentAmount"" = @Amount
WHERE ""id"" = @GoalId",
                    new
                    {
                        GoalId = goalId,
                        Amount = Math.Max(0m, amount),
                    },
                    cancellationToken: cancellationToken));
        }
    }

    private static string? PickGoalIdForCategory(IEnumerable<GoalSyncRow> goals, string category)
    {
        var candidates = goals
            .Where(goal => string.Equals(goal.Category, category, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (candidates.Length == 0)
        {
            return null;
        }

        if (candidates.Length == 1)
        {
            return candidates[0].Id;
        }

        var keywords = category.ToLowerInvariant() switch
        {
            "savings" => new[] { "saving", "savings" },
            "emergency" => new[] { "emergency" },
            "investment" => new[] { "invest", "investment" },
            _ => []
        };

        var matching = candidates
            .Where(goal =>
            {
                var normalizedTitle = NormalizeTitle(goal.Title);
                return keywords.Any(keyword => normalizedTitle.Contains(keyword, StringComparison.Ordinal));
            })
            .ToArray();

        return matching.Length == 1 ? matching[0].Id : null;
    }

    private static string NormalizeTitle(string? title) =>
        string.Join(' ', (title ?? string.Empty).Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static object ToSettingsPayload(PlanSettingsRow plan, ProfileMetaRow? profile)
    {
        var setupCompletedAt = LatestDate(
            profile?.SetupCompletedAt,
            string.Equals(profile?.SetupStatus, "completed", StringComparison.OrdinalIgnoreCase)
                ? profile?.SetupUpdatedAt
                : null);
        var payFrequency = NormalizePayFrequency(profile?.PayFrequency);

        return new
        {
            id = plan.Id,
            payDate = plan.PayDate,
            monthlyAllowance = plan.MonthlyAllowance,
            savingsBalance = plan.SavingsBalance,
            emergencyBalance = plan.EmergencyBalance,
            investmentBalance = plan.InvestmentBalance,
            monthlySavingsContribution = plan.MonthlySavingsContribution,
            monthlyEmergencyContribution = plan.MonthlyEmergencyContribution,
            monthlyInvestmentContribution = plan.MonthlyInvestmentContribution,
            budgetStrategy = plan.BudgetStrategy,
            budgetHorizonYears = plan.BudgetHorizonYears,
            incomeDistributeFullYearDefault = plan.IncomeDistributeFullYearDefault,
            incomeDistributeHorizonDefault = plan.IncomeDistributeHorizonDefault,
            homepageGoalIds = plan.HomepageGoalIds ?? [],
            country = plan.Country,
            language = plan.Language,
            currency = plan.Currency,
            accountCreatedAt = profile?.AccountCreatedAt,
            setupCompletedAt,
            payFrequency,
            billFrequency = DeriveBillFrequencyFromPayFrequency(payFrequency),
            payAnchorDate = profile?.PayAnchorDate?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        };
    }

    private static DateTimeOffset? LatestDate(params DateTimeOffset?[] dates)
    {
        var valid = dates.Where(date => date.HasValue).Select(date => date!.Value).ToArray();
        if (valid.Length == 0)
        {
            return null;
        }

        var latest = valid[0];
        for (var i = 1; i < valid.Length; i += 1)
        {
            if (valid[i] > latest)
            {
                latest = valid[i];
            }
        }

        return latest;
    }

    private static string NormalizePayFrequency(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "weekly" => "weekly",
            "every_2_weeks" => "every_2_weeks",
            "every_4_weeks" => "every_4_weeks",
            _ => "monthly",
        };
    }

    private static string NormalizeBillFrequency(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized == "every_2_weeks" ? "every_2_weeks" : "monthly";
    }

    private static string DeriveBillFrequencyFromPayFrequency(string? value)
    {
        return NormalizePayFrequency(value) == "every_2_weeks" ? "every_2_weeks" : "monthly";
    }

    private static bool TryParseDateOnlyLike(JsonElement element, out DateTime? value)
    {
        value = null;

        if (element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var raw = (element.GetString() ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            return true;
        }

        if (DateTime.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedDateOnly))
        {
            value = DateTime.SpecifyKind(parsedDateOnly.Date, DateTimeKind.Utc);
            return true;
        }

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedDateTime))
        {
            value = DateTime.SpecifyKind(parsedDateTime.Date, DateTimeKind.Utc);
            return true;
        }

        return false;
    }

    private static async Task<(bool Success, JsonElement Body, IResult? ErrorResult)> TryReadJsonBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        try
        {
            using var json = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
            if (json.RootElement.ValueKind != JsonValueKind.Object)
            {
                return (false, default, BffResults.BadRequest("Invalid JSON body"));
            }

            return (true, json.RootElement.Clone(), null);
        }
        catch (JsonException)
        {
            return (false, default, BffResults.BadRequest("Invalid JSON body"));
        }
    }

    private static bool TryReadDecimalProperty(JsonElement body, string propertyName, out decimal? value)
    {
        value = null;
        if (!body.TryGetProperty(propertyName, out var element))
        {
            return false;
        }

        if (!TryParseDecimal(element, out var parsed))
        {
            value = null;
            return true;
        }

        value = parsed;
        return true;
    }

    private static bool TryReadIntProperty(JsonElement body, string propertyName, out int? value)
    {
        value = null;
        if (!body.TryGetProperty(propertyName, out var element))
        {
            return false;
        }

        if (!TryParseInt(element, out var parsed))
        {
            value = null;
            return true;
        }

        value = parsed;
        return true;
    }

    private static bool TryReadBoolProperty(JsonElement body, string propertyName, out bool? value)
    {
        value = null;
        if (!body.TryGetProperty(propertyName, out var element))
        {
            return false;
        }

        if (!TryParseBool(element, out var parsed))
        {
            value = null;
            return true;
        }

        value = parsed;
        return true;
    }

    private static bool TryReadStringProperty(JsonElement body, string propertyName, out string? value)
    {
        value = null;
        if (!body.TryGetProperty(propertyName, out var element))
        {
            return false;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            value = null;
            return false;
        }

        value = element.GetString();
        return true;
    }

    private static string? ReadStringProperty(JsonElement body, string propertyName)
    {
        if (!body.TryGetProperty(propertyName, out var element))
        {
            return null;
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }

    private static string ReadStringLike(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => string.Empty,
            _ => element.ToString(),
        };
    }

    private static bool TryReadHomepageGoalIds(JsonElement body, out string[] goalIds)
    {
        goalIds = [];
        if (!body.TryGetProperty("homepageGoalIds", out var element))
        {
            return false;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var normalized = new List<string>(capacity: 2);

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var id = (item.GetString() ?? string.Empty).Trim();
            if (id.Length == 0 || !seen.Add(id))
            {
                continue;
            }

            normalized.Add(id);
            if (normalized.Count >= 2)
            {
                break;
            }
        }

        goalIds = normalized.ToArray();
        return true;
    }

    private static bool TryParseDecimal(JsonElement element, out decimal value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                if (element.TryGetDecimal(out value))
                {
                    return true;
                }

                if (element.TryGetDouble(out var asDouble))
                {
                    value = Convert.ToDecimal(asDouble, CultureInfo.InvariantCulture);
                    return true;
                }

                break;

            case JsonValueKind.String:
                var raw = element.GetString();
                if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
                {
                    return true;
                }

                break;
        }

        value = 0;
        return false;
    }

    private static bool TryParseInt(JsonElement element, out int value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                if (element.TryGetInt32(out value))
                {
                    return true;
                }

                break;

            case JsonValueKind.String:
                var raw = element.GetString();
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                {
                    return true;
                }

                break;
        }

        value = 0;
        return false;
    }

    private static bool TryParseBool(JsonElement element, out bool value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.True:
                value = true;
                return true;

            case JsonValueKind.False:
                value = false;
                return true;

            case JsonValueKind.String:
                var raw = element.GetString();
                if (bool.TryParse(raw, out value))
                {
                    return true;
                }

                break;
        }

        value = false;
        return false;
    }

    private sealed class PlanSettingsRow
    {
        public required string Id { get; init; }

        public int PayDate { get; init; }

        public decimal MonthlyAllowance { get; init; }

        public decimal SavingsBalance { get; init; }

        public decimal EmergencyBalance { get; init; }

        public decimal InvestmentBalance { get; init; }

        public decimal MonthlySavingsContribution { get; init; }

        public decimal MonthlyEmergencyContribution { get; init; }

        public decimal MonthlyInvestmentContribution { get; init; }

        public required string BudgetStrategy { get; init; }

        public int BudgetHorizonYears { get; init; }

        public bool IncomeDistributeFullYearDefault { get; init; }

        public bool IncomeDistributeHorizonDefault { get; init; }

        public string[] HomepageGoalIds { get; init; } = [];

        public required string Country { get; init; }

        public required string Language { get; init; }

        public required string Currency { get; init; }
    }

    private sealed class ProfileMetaRow
    {
        public DateTimeOffset? AccountCreatedAt { get; init; }

        public DateTimeOffset? SetupCompletedAt { get; init; }

        public DateTimeOffset? SetupUpdatedAt { get; init; }

        public string? SetupStatus { get; init; }

        public string? PayFrequency { get; init; }

        public string? BillFrequency { get; init; }

        public DateTimeOffset? PayAnchorDate { get; init; }
    }

    private sealed class GoalSyncRow
    {
        public required string Id { get; init; }

        public string? Title { get; init; }

        public required string Category { get; init; }
    }
}
