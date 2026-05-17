using BudgetinCheck.Api.Features.Common;
using BudgetinCheck.Api.Infrastructure.Auth;

namespace BudgetinCheck.Api.Features.BudgetSummary;

internal static class BudgetSummaryEndpoints
{
    public static async Task<IResult> GetAsync(
        HttpRequest request,
        CurrentSessionResolver sessionResolver,
        BudgetSummaryService service,
        CancellationToken cancellationToken)
    {
        var sessionResolution = await sessionResolver.ResolveAsync(request, cancellationToken);
        if (!sessionResolution.IsSuccess)
        {
            return sessionResolution.ToResult();
        }

        var budgetPlanId = sessionResolution.Session!.ResolveOwnedBudgetPlanId(request.Query["budgetPlanId"]);
        if (budgetPlanId is null)
        {
            return BffResults.BudgetPlanNotFound();
        }

        int? year = null;
        var rawYear = request.Query["year"].ToString();
        if (!string.IsNullOrWhiteSpace(rawYear))
        {
            if (!int.TryParse(rawYear, out var parsedYear))
            {
                return BffResults.BadRequest("Invalid year.");
            }

            year = parsedYear;
        }

        try
        {
            var summary = await service.GetAsync(
                budgetPlanId,
                request.Query["month"].ToString(),
                year,
                cancellationToken);

            return Results.Json(summary);
        }
        catch (ArgumentException error)
        {
            return BffResults.BadRequest(error.Message);
        }
        catch (KeyNotFoundException)
        {
            return BffResults.BudgetPlanNotFound();
        }
        catch (Exception)
        {
            return BffResults.InternalServerError("Failed to compute budget summary");
        }
    }
}