using BudgetinCheck.Api.Features.Common;
using BudgetinCheck.Api.Infrastructure.Auth;
using BudgetinCheck.Api.Infrastructure.Data;
using Dapper;

namespace BudgetinCheck.Api.Features.BudgetPlans;

internal static class BudgetPlansEndpoints
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

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        var plans = await connection.QueryAsync<BudgetPlanListRow>(
            new CommandDefinition(
                @"
SELECT
    ""id"" AS ""Id"",
    ""name"" AS ""Name"",
    ""kind""::text AS ""Kind"",
    ""payDate"" AS ""PayDate"",
    ""budgetHorizonYears"" AS ""BudgetHorizonYears"",
    ""createdAt"" AS ""CreatedAt""
FROM ""BudgetPlan""
WHERE ""userId"" = @UserId
ORDER BY ""createdAt"" DESC",
                new { UserId = sessionResolution.Session!.UserId },
                cancellationToken: cancellationToken));

        return Results.Json(new
        {
            plans = plans.Select(plan => new
            {
                id = plan.Id,
                name = plan.Name,
                kind = plan.Kind,
                payDate = plan.PayDate,
                budgetHorizonYears = plan.BudgetHorizonYears,
                createdAt = plan.CreatedAt,
            }),
        });
    }

    private sealed class BudgetPlanListRow
    {
        public required string Id { get; init; }

        public required string Name { get; init; }

        public required string Kind { get; init; }

        public int PayDate { get; init; }

        public int BudgetHorizonYears { get; init; }

        public DateTimeOffset CreatedAt { get; init; }
    }
}