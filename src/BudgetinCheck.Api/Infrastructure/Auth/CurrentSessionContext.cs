namespace BudgetinCheck.Api.Infrastructure.Auth;

internal sealed record CurrentSessionContext(string UserId, IReadOnlyList<BudgetPlanScope> Plans)
{
    public string? ResolveOwnedBudgetPlanId(string? requestedBudgetPlanId)
    {
        var normalized = requestedBudgetPlanId?.Trim();
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return Plans.Any(plan => string.Equals(plan.Id, normalized, StringComparison.Ordinal))
                ? normalized
                : null;
        }

        var personal = Plans
            .Where(plan => string.Equals(plan.Kind, "personal", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(plan => plan.CreatedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();

        if (personal is not null)
        {
            return personal.Id;
        }

        return Plans
            .OrderByDescending(plan => plan.CreatedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault()
            ?.Id;
    }
}

internal sealed record BudgetPlanScope(string Id, string? Kind, DateTimeOffset? CreatedAt);

internal sealed record CurrentSessionResolution(CurrentSessionContext? Session, string? Error, int StatusCode)
{
    public bool IsSuccess => Session is not null;

    public IResult ToResult() => StatusCode switch
    {
        StatusCodes.Status401Unauthorized => Results.Json(new { error = Error ?? "Not authenticated" }, statusCode: StatusCodes.Status401Unauthorized),
        StatusCodes.Status503ServiceUnavailable => Results.Json(new { error = Error ?? "Authentication bridge unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable),
        _ => Results.Json(new { error = Error ?? "Request failed" }, statusCode: StatusCode),
    };

    public static CurrentSessionResolution Success(CurrentSessionContext session) => new(session, null, StatusCodes.Status200OK);

    public static CurrentSessionResolution Unauthorized() => new(null, "Not authenticated", StatusCodes.Status401Unauthorized);

    public static CurrentSessionResolution Unavailable(string message) => new(null, message, StatusCodes.Status503ServiceUnavailable);
}