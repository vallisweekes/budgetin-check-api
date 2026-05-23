using BudgetinCheck.Api.Features.Common;
using BudgetinCheck.Api.Infrastructure.Auth;

namespace BudgetinCheck.Api.Features.ExpenseSummary;

internal static class ExpenseSummaryEndpoints
{
    public static async Task<IResult> GetAsync(
        HttpContext context,
        CurrentSessionResolver sessionResolver,
        ExpenseSummaryService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("ExpenseSummaryEndpoints");

        var sessionResolution = await sessionResolver.ResolveAsync(context.Request, cancellationToken);
        if (!sessionResolution.IsSuccess)
        {
            return sessionResolution.ToResult();
        }

        var budgetPlanId = sessionResolution.Session!.ResolveOwnedBudgetPlanId(context.Request.Query["budgetPlanId"]);
        if (budgetPlanId is null)
        {
            return BffResults.BudgetPlanNotFound();
        }

        if (!TryParseMonth(context.Request.Query["month"], out var month))
        {
            return BffResults.BadRequest("Invalid month.");
        }

        if (!TryParseYear(context.Request.Query["year"], out var year))
        {
            return BffResults.BadRequest("Invalid year.");
        }

        var scope = ParseScope(context.Request.Query["scope"]);
        if (scope is null)
        {
            return BffResults.BadRequest("Invalid scope.");
        }

        var includeBudgetOverview = ParseBoolean(context.Request.Query["includeBudgetOverview"]);

        try
        {
            var summary = await service.GetAsync(
                budgetPlanId,
                month,
                year,
                scope,
                includeBudgetOverview,
                cancellationToken);

            return Results.Json(summary);
        }
        catch (KeyNotFoundException)
        {
            return BffResults.BudgetPlanNotFound();
        }
        catch (ArgumentException error)
        {
            return BffResults.BadRequest(error.Message);
        }
        catch (Exception error)
        {
            logger.LogError(error, "Failed to compute expense summary for budgetPlanId {BudgetPlanId}", budgetPlanId);
            return BffResults.InternalServerError("Failed to compute expense summary");
        }
    }

    private static bool TryParseMonth(string? raw, out int month)
    {
        if (!int.TryParse(raw, out month))
        {
            return false;
        }

        return month is >= 1 and <= 12;
    }

    private static bool TryParseYear(string? raw, out int year)
    {
        if (!int.TryParse(raw, out year))
        {
            return false;
        }

        return year >= 1900;
    }

    private static string? ParseScope(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "month";
        if (string.Equals(raw, "month", StringComparison.OrdinalIgnoreCase)) return "month";
        if (string.Equals(raw, "pay_period", StringComparison.OrdinalIgnoreCase)) return "pay_period";
        return null;
    }

    private static bool ParseBoolean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        return string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase);
    }
}