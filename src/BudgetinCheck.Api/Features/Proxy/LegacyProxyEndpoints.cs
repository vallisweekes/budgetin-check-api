using BudgetinCheck.Api.Features.Common;
using BudgetinCheck.Api.Infrastructure.Legacy;

namespace BudgetinCheck.Api.Features.Proxy;

internal static class LegacyProxyEndpoints
{
    public static async Task<IResult> ProxyAsync(HttpContext context, LegacyBffClient legacyClient, CancellationToken cancellationToken)
    {
        if (!legacyClient.IsConfigured)
        {
            return BffResults.ServiceUnavailable("LegacyNextJs:BaseUrl is not configured.");
        }

        await legacyClient.ProxyAsync(context, cancellationToken);
        return Results.Empty;
    }
}