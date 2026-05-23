using BudgetinCheck.Api.Features.Common;
using BudgetinCheck.Api.Infrastructure.Auth;
using BudgetinCheck.Api.Infrastructure.Legacy;

namespace BudgetinCheck.Api.Features.IncomeMonth;

internal static class IncomeMonthEndpoints
{
    public static async Task<IResult> GetAsync(
        HttpContext context,
        CurrentSessionResolver sessionResolver,
        IncomeMonthService service,
        LegacyBffClient legacyClient,
        CancellationToken cancellationToken)
    {
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

        if (!TryParseOptionalMonth(context.Request.Query["month"].ToString(), out var month))
        {
            return BffResults.BadRequest("Invalid month.");
        }

        if (!TryParseOptionalYear(context.Request.Query["year"].ToString(), out var year))
        {
            return BffResults.BadRequest("Invalid year.");
        }

        var mode = ParseMode(context.Request.Query["mode"].ToString());
        if (mode is null)
        {
            return BffResults.BadRequest("Invalid mode.");
        }

        try
        {
            var response = await service.GetAsync(budgetPlanId, month, year, mode, cancellationToken);
            return Results.Json(response);
        }
        catch (KeyNotFoundException)
        {
            return BffResults.BudgetPlanNotFound();
        }
        catch (ArgumentException error)
        {
            return BffResults.BadRequest(error.Message);
        }
        catch (Exception) when (legacyClient.IsConfigured)
        {
            await legacyClient.ProxyAsync(context, cancellationToken);
            return Results.Empty;
        }
        catch (Exception)
        {
            return BffResults.InternalServerError("Failed to load income month data");
        }
    }

    private static bool TryParseOptionalMonth(string? raw, out int? month)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            month = null;
            return true;
        }

        if (int.TryParse(raw, out var parsed) && parsed is >= 1 and <= 12)
        {
            month = parsed;
            return true;
        }

        month = null;
        return false;
    }

    private static bool TryParseOptionalYear(string? raw, out int? year)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            year = null;
            return true;
        }

        if (int.TryParse(raw, out var parsed) && parsed >= 1900)
        {
            year = parsed;
            return true;
        }

        year = null;
        return false;
    }

    private static string? ParseMode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "full";
        if (string.Equals(raw, "full", StringComparison.OrdinalIgnoreCase)) return "full";
        if (string.Equals(raw, "home_core", StringComparison.OrdinalIgnoreCase)) return "home_core";
        return null;
    }
}