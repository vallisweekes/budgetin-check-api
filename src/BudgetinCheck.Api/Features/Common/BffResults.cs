namespace BudgetinCheck.Api.Features.Common;

internal static class BffResults
{
    public static IResult Unauthorized() => Results.Json(new { error = "Not authenticated" }, statusCode: StatusCodes.Status401Unauthorized);

    public static IResult BudgetPlanNotFound() => Results.Json(new { error = "Budget plan not found" }, statusCode: StatusCodes.Status404NotFound);

    public static IResult BadRequest(string message) => Results.Json(new { error = message }, statusCode: StatusCodes.Status400BadRequest);

    public static IResult NotFound(string message) => Results.Json(new { error = message }, statusCode: StatusCodes.Status404NotFound);

    public static IResult ServiceUnavailable(string message) => Results.Json(new { error = message }, statusCode: StatusCodes.Status503ServiceUnavailable);

    public static IResult InternalServerError(string message) => Results.Json(new { error = message }, statusCode: StatusCodes.Status500InternalServerError);
}