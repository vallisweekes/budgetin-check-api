using BudgetinCheck.Api.Infrastructure.Legacy;

namespace BudgetinCheck.Api.Infrastructure.Auth;

internal sealed class CurrentSessionResolver(LegacyBffClient legacyBffClient)
{
    public async Task<CurrentSessionResolution> ResolveAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!legacyBffClient.IsConfigured)
        {
            return CurrentSessionResolution.Unavailable("LegacyNextJs:BaseUrl is not configured.");
        }

        try
        {
            var me = await legacyBffClient.GetProfileAsync(request, cancellationToken);
            if (me is null || string.IsNullOrWhiteSpace(me.Id))
            {
                return CurrentSessionResolution.Unauthorized();
            }

            var plans = (me.Plans ?? [])
                .Where(plan => !string.IsNullOrWhiteSpace(plan.Id))
                .Select(plan => new BudgetPlanScope(plan.Id!, plan.Kind, plan.CreatedAt))
                .ToArray();

            return CurrentSessionResolution.Success(new CurrentSessionContext(me.Id, plans));
        }
        catch (HttpRequestException error)
        {
            return CurrentSessionResolution.Unavailable($"Legacy auth bridge request failed: {error.Message}");
        }
        catch (TaskCanceledException error)
        {
            return CurrentSessionResolution.Unavailable($"Legacy auth bridge request timed out: {error.Message}");
        }
    }
}