using BudgetinCheck.Api.Infrastructure.Auth;
using BudgetinCheck.Api.Infrastructure.Legacy;

namespace BudgetinCheck.Api.Features.IncomeSacrifice;

internal static class IncomeSacrificeEndpoints
{
    public static Task<IResult> GetAsync(
        HttpContext context,
        CurrentSessionResolver sessionResolver,
        LegacyBffClient legacyClient,
        CancellationToken cancellationToken) => ProxyAuthenticatedAsync(context, sessionResolver, legacyClient, cancellationToken);

    public static Task<IResult> PatchAsync(
        HttpContext context,
        CurrentSessionResolver sessionResolver,
        LegacyBffClient legacyClient,
        CancellationToken cancellationToken) => ProxyAuthenticatedAsync(context, sessionResolver, legacyClient, cancellationToken);

    public static Task<IResult> CreateCustomAsync(
        HttpContext context,
        CurrentSessionResolver sessionResolver,
        LegacyBffClient legacyClient,
        CancellationToken cancellationToken) => ProxyAuthenticatedAsync(context, sessionResolver, legacyClient, cancellationToken);

    public static Task<IResult> DeleteCustomAsync(
        HttpContext context,
        CurrentSessionResolver sessionResolver,
        LegacyBffClient legacyClient,
        CancellationToken cancellationToken) => ProxyAuthenticatedAsync(context, sessionResolver, legacyClient, cancellationToken);

    public static Task<IResult> GetGoalsAsync(
        HttpContext context,
        CurrentSessionResolver sessionResolver,
        LegacyBffClient legacyClient,
        CancellationToken cancellationToken) => ProxyAuthenticatedAsync(context, sessionResolver, legacyClient, cancellationToken);

    public static Task<IResult> PatchGoalsAsync(
        HttpContext context,
        CurrentSessionResolver sessionResolver,
        LegacyBffClient legacyClient,
        CancellationToken cancellationToken) => ProxyAuthenticatedAsync(context, sessionResolver, legacyClient, cancellationToken);

    public static Task<IResult> PostGoalsAsync(
        HttpContext context,
        CurrentSessionResolver sessionResolver,
        LegacyBffClient legacyClient,
        CancellationToken cancellationToken) => ProxyAuthenticatedAsync(context, sessionResolver, legacyClient, cancellationToken);

    private static async Task<IResult> ProxyAuthenticatedAsync(
        HttpContext context,
        CurrentSessionResolver sessionResolver,
        LegacyBffClient legacyClient,
        CancellationToken cancellationToken)
    {
        var sessionResolution = await sessionResolver.ResolveAsync(context.Request, cancellationToken);
        if (!sessionResolution.IsSuccess)
        {
            return sessionResolution.ToResult();
        }

        await legacyClient.ProxyAsync(context, cancellationToken);
        return Results.Empty;
    }
}